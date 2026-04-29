using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace STS2AILogger.STS2AILoggerCode.Logging;

public static class EventLogger
{
    private const int SchemaVersion = 4;
    private const string LogVersion = "alpha.1";
    private const int MaxInGameEventDescriptionLength = 220;

    private static readonly object Lock = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private static string? _loggerDir;
    private static string? _logPath;
    private static string? _sessionId;
    private static string? _runId;
    private static string? _runKey;
    private static LoggerLaunchOptions? _launchOptions;
    private static long _eventIndex;
    private static string? _deckMutationCachePath;
    private static RewindDetection? _pendingRewindDetection;
    private static readonly HashSet<string> DeckMutationKeys = new();

    public static string? LogPath => _logPath;
    public static string? SessionId => _sessionId;
    public static string? RunId => _runId;
    public static string? RunKey => _runKey;
    public static string DataType => (_launchOptions ??= LoggerLaunchOptions.FromProcess()).DataType;
    public static string Version => LogVersion;
    public static bool CurrentRunHasPriorEvents => _runId != null && _eventIndex > 1;
    public static long NextEventIndex => _eventIndex;

    public static void Info(string message)
    {
        MainFile.Logger.Info(message);
    }

    public static void Debug(string message)
    {
        MainFile.Logger.Debug(message);
    }

    public static void Error(string message)
    {
        MainFile.Logger.Error(message);
    }

    public static void Initialize()
    {
        try
        {
            string userDir = ProjectSettings.GlobalizePath("user://");
            _loggerDir = Path.Combine(userDir, "STS2AILogger");
            Directory.CreateDirectory(_loggerDir);
            Directory.CreateDirectory(Path.Combine(_loggerDir, "sessions"));
            Directory.CreateDirectory(Path.Combine(_loggerDir, "runs"));

            string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            _sessionId = $"session-{timestamp}";
            _logPath = Path.Combine(_loggerDir, "sessions", $"{_sessionId}.jsonl");
            _launchOptions ??= LoggerLaunchOptions.FromProcess();
            _eventIndex = 0;

            Write("logger_initialized", new
            {
                log_path = _logPath,
                session_id = _sessionId,
                schema_version = SchemaVersion,
                mod_log_version = LogVersion,
                data_type = DataType
            }, "logger initialized");

            Info($"STS2AILogger {LogVersion} session={_sessionId} data_type={DataType}");
        }
        catch (Exception ex)
        {
            Error($"Failed to initialize event logger: {ex}");
        }
    }

    public static void SetRunContext(RunState? runState, bool allowLateEventContinuation = false)
    {
        try
        {
            if (runState == null)
            {
                return;
            }

            if (_loggerDir == null || _sessionId == null)
            {
                Initialize();
            }

            string runKey = RunLogContext.BuildRunKey(runState);
            string runId = runKey;
            string runLogPath = RunLogContext.BuildRunPath(_loggerDir!, runId);
            bool isLateEventContinuation = allowLateEventContinuation && _runKey == runKey && _runId == runKey;
            if (!isLateEventContinuation && RunLogContext.HasRunEnded(runLogPath))
            {
                runId = RunLogContext.BuildRerunId(runKey, _sessionId!);
                runLogPath = RunLogContext.BuildRunPath(_loggerDir!, runId);
            }

            if (_runId == runId)
            {
                return;
            }

            bool isContinuing = File.Exists(runLogPath) && new FileInfo(runLogPath).Length > 0;
            _pendingRewindDetection = isContinuing ? BuildRewindDetection(runLogPath, runState) : null;

            _runId = runId;
            _runKey = runKey;
            _logPath = runLogPath;
            _eventIndex = CountLines(runLogPath);
            LoadDeckMutationCache(runLogPath);

            Info($"run log: {_logPath}{(isContinuing ? " (continuing)" : "")}");

            Write("run_log_attached", new
            {
                run_id = _runId,
                run_key = _runKey,
                session_id = _sessionId,
                schema_version = SchemaVersion,
                mod_log_version = LogVersion,
                data_type = DataType,
                log_path = _logPath,
                continuing = isContinuing,
                is_late_event_continuation = isLateEventContinuation,
                seed = runState.Rng.StringSeed,
                game_mode = runState.GameMode.ToString(),
                ascension = runState.AscensionLevel,
                characters = runState.Players.Select(player => player.Character.Id.ToString()).ToList()
            }, isContinuing ? $"continuing run log: {_runId}" : $"created run log: {_runId}");
        }
        catch (Exception ex)
        {
            Error($"Failed to set run log context: {ex}");
        }
    }

    public static bool IsRunEnded(RunState? runState)
    {
        try
        {
            if (runState == null || _loggerDir == null)
            {
                return false;
            }

            string runKey = RunLogContext.BuildRunKey(runState);
            if (_runKey == runKey && _logPath != null && RunLogContext.HasRunEnded(_logPath))
            {
                return true;
            }

            string runLogPath = RunLogContext.BuildRunPath(_loggerDir, runKey);
            return RunLogContext.HasRunEnded(runLogPath);
        }
        catch (Exception ex)
        {
            Error($"Failed to check run ended state: {ex}");
            return false;
        }
    }

    public static void WritePendingStateRewindIfDetected(long loadedEventIndex)
    {
        try
        {
            RewindDetection? detection = _pendingRewindDetection;
            _pendingRewindDetection = null;

            if (detection == null)
            {
                return;
            }

            Write("state_rewind_detected", new
            {
                previous_event_index = detection.Previous.EventIndex,
                previous_event_type = detection.Previous.EventType,
                previous_summary = detection.Previous.Summary,
                loaded_event_index = loadedEventIndex,
                last_matching_event_index = detection.LastMatching?.EventIndex,
                invalidated_from_event_index = detection.InvalidatedFromEventIndex,
                invalidated_to_event_index = detection.Previous.EventIndex,
                differences = detection.Differences
            }, "loaded state differs from previous logged state");
        }
        catch (Exception ex)
        {
            Error($"Failed to write state rewind detection: {ex}");
        }
    }

    public static void PrepareStateRewindDetection(RunState? loadedRunState)
    {
        try
        {
            if (loadedRunState == null || _logPath == null || !File.Exists(_logPath))
            {
                return;
            }

            _pendingRewindDetection ??= BuildRewindDetection(_logPath, loadedRunState);
        }
        catch (Exception ex)
        {
            Error($"Failed to prepare state rewind detection: {ex}");
        }
    }

    public static bool TryRememberDeckMutation(string type, Player owner, CardModel card, IEnumerable<CardModel> deck)
    {
        try
        {
            if (_logPath == null)
            {
                Initialize();
            }

            EnsureDeckMutationCacheLoaded();

            string key = BuildDeckMutationKey(type, owner.NetId, BuildCardSignature(card), BuildDeckSignature(deck));
            if (DeckMutationKeys.Add(key))
            {
                return true;
            }

            Debug($"skipping duplicate deck mutation: {type} {card.Id}");
            return false;
        }
        catch (Exception ex)
        {
            Error($"Failed to deduplicate deck mutation '{type}': {ex}");
            return true;
        }
    }

    public static void Write(string type, object? payload = null, string? summary = null)
    {
        try
        {
            if (_logPath == null)
            {
                Initialize();
            }

            long eventIndex = _eventIndex++;
            var envelope = new Dictionary<string, object?>
            {
                ["schema_version"] = SchemaVersion,
                ["event_index"] = eventIndex,
                ["timestamp_utc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["session_id"] = _sessionId,
                ["run_id"] = _runId,
                ["run_key"] = _runKey,
                ["data_type"] = DataType,
                ["type"] = type,
                ["summary"] = summary,
                ["payload"] = payload
            };

            string line = JsonSerializer.Serialize(envelope, JsonOptions);

            lock (Lock)
            {
                File.AppendAllText(_logPath!, line + System.Environment.NewLine);
            }

            Info(BuildInGameEventLog(eventIndex, type, summary, payload));
        }
        catch (Exception ex)
        {
            Error($"Failed to write event '{type}': {ex}");
        }
    }

    private static string BuildInGameEventLog(long eventIndex, string type, string? summary, object? payload)
    {
        string description = BuildCompactEventDescription(type, payload);
        if (string.IsNullOrWhiteSpace(description))
        {
            description = summary ?? "event written";
        }

        string message = $"#{eventIndex} {type}";
        message += string.IsNullOrWhiteSpace(description) ? "" : $" {description}";

        if (message.Length <= MaxInGameEventDescriptionLength)
        {
            return message;
        }

        return message[..(MaxInGameEventDescriptionLength - 3)] + "...";
    }

    private static string BuildCompactEventDescription(string type, object? payload)
    {
        if (payload == null)
        {
            return "";
        }

        try
        {
            JsonElement root = JsonSerializer.SerializeToElement(payload, JsonOptions);
            return type switch
            {
                "logger_initialized" => $"session={JsonString(root, "session_id")}",
                "run_log_attached" => $"run={JsonString(root, "run_id")} seed={JsonString(root, "seed")}",
                "run_started" => CompactRunState(root, "start"),
                "run_loaded" => CompactRunState(root, "load"),
                "state_rewind_detected" => $"rewind {JsonInt(root, "invalidated_from_event_index")}-{JsonInt(root, "invalidated_to_event_index")}",
                "room_entered" => CompactRoom(root),
                "combat_setup" => CompactCombat(root, "setup"),
                "turn_started" => CompactCombat(root, "turn"),
                "turn_ended" => CompactTurnEnded(root),
                "combat_ended" => CompactCombatEnded(root),
                "player_ended_turn" => CompactPlayerEndedTurn(root),
                "card_play_started" => CompactCardPlay(root, ">"),
                "card_play_finished" => CompactCardPlay(root, "done"),
                "card_selection_offered" => CompactCardSelectionOffered(root),
                "card_selection_selected" => CompactCardSelectionSelected(root),
                "deck_card_added" => CompactDeckMutation(root, "+deck"),
                "deck_card_removed" => CompactDeckMutation(root, "-deck"),
                "event_options_offered" => CompactEventOptions(root),
                "event_option_selected" => CompactEventOption(root),
                "potion_use_started" => CompactPotion(root, "potion_start"),
                "potion_used" => CompactPotion(root, "potion_used"),
                "potion_discarded" => CompactPotion(root, "potion_discard"),
                "shop_items_offered" => CompactShopOffered(root),
                "shop_item_purchased" => CompactShopPurchased(root),
                "run_ended" => CompactRunEnded(root),
                "map_nodes_offered" => CompactMapNodesOffered(root),
                "map_node_selected" => CompactMapNodeSelected(root),
                "map_node_resolved" => CompactMapNodeResolved(root),
                "treasure_relics_offered" => CompactTreasureRelicsOffered(root),
                "treasure_relic_selected" => CompactTreasureRelicResult(root, "pick"),
                "treasure_relic_skipped" => CompactTreasureRelicResult(root, "skip"),
                _ => ""
            };
        }
        catch (Exception ex)
        {
            return $"description_failed={ex.GetType().Name}";
        }
    }

    private static string BuildEventDescription(string type, object? payload)
    {
        if (payload == null)
        {
            return "";
        }

        try
        {
            JsonElement root = JsonSerializer.SerializeToElement(payload, JsonOptions);
            return type switch
            {
                "logger_initialized" => DescribeLoggerInitialized(root),
                "run_log_attached" => DescribeRunLogAttached(root),
                "run_started" => DescribeRunStateEvent(root, "started"),
                "run_loaded" => DescribeRunStateEvent(root, "loaded"),
                "state_rewind_detected" => DescribeStateRewind(root),
                "room_entered" => DescribeRoomEntered(root),
                "combat_setup" => DescribeCombatEvent(root, "setup"),
                "turn_started" => DescribeCombatEvent(root, "turn started"),
                "turn_ended" => DescribeCombatEvent(root, "turn ended"),
                "combat_ended" => DescribeCombatEnded(root),
                "player_ended_turn" => DescribePlayerEndedTurn(root),
                "card_play_started" => DescribeCardPlay(root, "started"),
                "card_play_finished" => DescribeCardPlay(root, "finished"),
                "card_selection_offered" => DescribeCardSelectionOffered(root),
                "card_selection_selected" => DescribeCardSelectionSelected(root),
                "deck_card_added" => DescribeDeckMutation(root, "added"),
                "deck_card_removed" => DescribeDeckMutation(root, "removed"),
                "event_options_offered" => DescribeEventOptionsOffered(root),
                "event_option_selected" => DescribeEventOptionSelected(root),
                "potion_use_started" => DescribePotionUseStarted(root),
                "potion_used" => DescribePotionUsed(root),
                "potion_discarded" => DescribePotionDiscarded(root),
                "shop_items_offered" => DescribeShopItemsOffered(root),
                "shop_item_purchased" => DescribeShopItemPurchased(root),
                "run_ended" => DescribeRunEnded(root),
                _ => ""
            };
        }
        catch (Exception ex)
        {
            return $"event written; description failed: {ex.GetType().Name}";
        }
    }

    private static string DescribeLoggerInitialized(JsonElement root)
    {
        return $"session log initialized at {JsonString(root, "log_path")}";
    }

    private static string CompactRunState(JsonElement root, string action)
    {
        TryGetObject(root, "run", out JsonElement run);
        TryGetObject(root, "local_player", out JsonElement player);
        return $"{action} floor={JsonInt(run, "total_floor")} hp={JsonInt(player, "hp")}/{JsonInt(player, "max_hp")} gold={JsonInt(player, "gold")}";
    }

    private static string CompactRoom(JsonElement root)
    {
        TryGetObject(root, "run", out JsonElement run);
        return $"floor={JsonInt(run, "total_floor")} room={DescribeRoomFromRun(run)}";
    }

    private static string CompactCombat(JsonElement root, string action)
    {
        TryGetObject(root, "combat", out JsonElement combat);
        return $"{action} {JsonString(combat, "encounter")} r{JsonInt(combat, "round")} side={JsonString(combat, "current_side")}";
    }

    private static string CompactTurnEnded(JsonElement root)
    {
        TryGetObject(root, "combat", out JsonElement combat);
        string endedSide = JsonString(root, "ended_side");
        return $"ended={endedSide} r{JsonInt(combat, "round")} now={JsonString(root, "current_side")}";
    }

    private static string CompactCombatEnded(JsonElement root)
    {
        TryGetObject(root, "run", out JsonElement run);
        TryGetObject(root, "local_player", out JsonElement player);
        return $"floor={JsonInt(run, "total_floor")} hp={JsonInt(player, "hp")}/{JsonInt(player, "max_hp")}";
    }

    private static string CompactPlayerEndedTurn(JsonElement root)
    {
        TryGetObject(root, "player", out JsonElement player);
        TryGetObject(root, "combat", out JsonElement combat);
        return $"player={JsonInt(player, "net_id")} r{JsonInt(combat, "round")} energy={JsonInt(player, "energy")}";
    }

    private static string CompactCardPlay(JsonElement root, string action)
    {
        TryGetObject(root, "card_play", out JsonElement cardPlay);
        TryGetObject(cardPlay, "card", out JsonElement card);
        TryGetObject(cardPlay, "target", out JsonElement target);
        string targetText = target.ValueKind == JsonValueKind.Object ? $" -> {JsonString(target, "name")}" : "";
        return $"{action} {JsonString(card, "id")}{targetText}";
    }

    private static string CompactCardSelectionOffered(JsonElement root)
    {
        return $"{JsonString(root, "selection_kind")} id={JsonString(root, "selection_id")} choices={JsonArrayLength(root, "candidates")}";
    }

    private static string CompactCardSelectionSelected(JsonElement root)
    {
        return $"{JsonString(root, "selection_kind")} id={JsonString(root, "selection_id")} pick=[{CompactSelectedCards(root)}]";
    }

    private static string CompactDeckMutation(JsonElement root, string action)
    {
        TryGetObject(root, "card", out JsonElement card);
        return $"{action} {JsonString(card, "id")}";
    }

    private static string CompactEventOptions(JsonElement root)
    {
        TryGetObject(root, "event", out JsonElement eventElement);
        return $"{JsonString(eventElement, "id")} options={JsonArrayLength(root, "options")}";
    }

    private static string CompactEventOption(JsonElement root)
    {
        TryGetObject(root, "event", out JsonElement eventElement);
        return $"{JsonString(eventElement, "id")} pick={JsonInt(root, "option_index")}";
    }

    private static string CompactPotion(JsonElement root, string action)
    {
        TryGetObject(root, "potion", out JsonElement potion);
        return $"{action} {JsonString(potion, "id")}";
    }

    private static string CompactShopOffered(JsonElement root)
    {
        TryGetObject(root, "shop", out JsonElement shop);
        return $"cards={JsonArrayLength(shop, "cards")} relics={JsonArrayLength(shop, "relics")} potions={JsonArrayLength(shop, "potions")}";
    }

    private static string CompactShopPurchased(JsonElement root)
    {
        TryGetObject(root, "item", out JsonElement item);
        return $"{DescribeShopEntry(item)} spent={JsonInt(root, "gold_spent")}";
    }

    private static string CompactRunEnded(JsonElement root)
    {
        TryGetObject(root, "run", out JsonElement run);
        TryGetObject(root, "local_player", out JsonElement player);
        return $"victory={JsonBool(root, "victory")} floor={JsonInt(run, "total_floor")} hp={JsonInt(player, "hp")}";
    }

    private static string CompactMapNodesOffered(JsonElement root)
    {
        return $"id={JsonString(root, "selection_id")} choices={JsonArrayLength(root, "available_nodes")}";
    }

    private static string CompactMapNodeSelected(JsonElement root)
    {
        TryGetObject(root, "selected", out JsonElement selected);
        TryGetObject(selected, "coord", out JsonElement coord);
        return $"id={JsonString(root, "selection_id")} coord=({JsonInt(coord, "row")},{JsonInt(coord, "col")}) type={JsonString(selected, "map_point_type_before_reveal")}";
    }

    private static string CompactMapNodeResolved(JsonElement root)
    {
        TryGetObject(root, "resolved_room", out JsonElement room);
        return $"id={JsonString(root, "selection_id")} -> {JsonString(room, "type")} {JsonString(room, "model")}";
    }

    private static string CompactTreasureRelicsOffered(JsonElement root)
    {
        return $"id={JsonString(root, "selection_id")} relics={JsonArrayLength(root, "candidates")} skip={JsonBool(root, "can_skip")}";
    }

    private static string CompactTreasureRelicResult(JsonElement root, string action)
    {
        return $"{action} id={JsonString(root, "selection_id")} result={JsonString(root, "selection_result")}";
    }

    private static string DescribeRunLogAttached(JsonElement root)
    {
        string action = JsonBool(root, "continuing") == true ? "continuing" : "created";
        string characters = JsonStringArray(root, "characters").DefaultIfEmpty("unknown").Aggregate((left, right) => $"{left},{right}");
        return $"{action} run log {JsonString(root, "run_id")} seed={JsonString(root, "seed")} ascension={JsonInt(root, "ascension")} mode={JsonString(root, "game_mode")} characters={characters}";
    }

    private static string DescribeRunStateEvent(JsonElement root, string action)
    {
        if (!TryGetObject(root, "run", out JsonElement run))
        {
            return $"run {action}";
        }

        TryGetObject(root, "local_player", out JsonElement player);
        string character = JsonString(player, "character");
        int deckCount = JsonArrayLength(player, "deck");
        int relicCount = JsonArrayLength(player, "relics");
        string room = DescribeRoomFromRun(run);
        return $"run {action}: seed={JsonString(run, "seed")} floor={JsonInt(run, "total_floor")} room={room} character={character} hp={JsonInt(player, "hp")}/{JsonInt(player, "max_hp")} gold={JsonInt(player, "gold")} deck={deckCount} relics={relicCount}";
    }

    private static string DescribeStateRewind(JsonElement root)
    {
        string fields = "";
        if (root.TryGetProperty("differences", out JsonElement differences) && differences.ValueKind == JsonValueKind.Array)
        {
            fields = string.Join(",", differences.EnumerateArray().Select(difference => JsonString(difference, "field")).Where(field => !string.IsNullOrWhiteSpace(field)));
        }

        return $"SL rewind detected: previous_event={JsonInt(root, "previous_event_index")} loaded_event={JsonInt(root, "loaded_event_index")} invalidated={JsonInt(root, "invalidated_from_event_index")}-{JsonInt(root, "invalidated_to_event_index")} fields={fields}";
    }

    private static string DescribeRoomEntered(JsonElement root)
    {
        if (!TryGetObject(root, "run", out JsonElement run))
        {
            return "room entered";
        }

        TryGetObject(root, "local_player", out JsonElement player);
        return $"room entered: floor={JsonInt(run, "total_floor")} room={DescribeRoomFromRun(run)} hp={JsonInt(player, "hp")}/{JsonInt(player, "max_hp")} gold={JsonInt(player, "gold")}";
    }

    private static string DescribeCombatEvent(JsonElement root, string action)
    {
        if (!TryGetObject(root, "combat", out JsonElement combat))
        {
            return $"combat {action}";
        }

        string enemies = DescribeEnemies(combat);
        TryGetObject(root, "local_player", out JsonElement player);
        return $"combat {action}: encounter={JsonString(combat, "encounter")} round={JsonInt(combat, "round")} side={JsonString(combat, "side")} hp={JsonInt(player, "hp")}/{JsonInt(player, "max_hp")} block={JsonInt(player, "block")} energy={JsonInt(player, "energy")} enemies=[{enemies}]";
    }

    private static string DescribeCombatEnded(JsonElement root)
    {
        TryGetObject(root, "run", out JsonElement run);
        TryGetObject(root, "local_player", out JsonElement player);
        string room = DescribeRoomFromRun(run);
        return $"combat ended: floor={JsonInt(run, "total_floor")} room={room} hp={JsonInt(player, "hp")}/{JsonInt(player, "max_hp")} gold={JsonInt(player, "gold")}";
    }

    private static string DescribePlayerEndedTurn(JsonElement root)
    {
        TryGetObject(root, "player", out JsonElement player);
        TryGetObject(root, "combat", out JsonElement combat);
        return $"player ended turn: player={JsonInt(player, "net_id")} encounter={JsonString(combat, "encounter")} round={JsonInt(combat, "round")} hp={JsonInt(player, "hp")}/{JsonInt(player, "max_hp")} block={JsonInt(player, "block")} energy={JsonInt(player, "energy")}";
    }

    private static string DescribeCardPlay(JsonElement root, string action)
    {
        if (!TryGetObject(root, "card_play", out JsonElement cardPlay))
        {
            return $"card play {action}";
        }

        TryGetObject(cardPlay, "card", out JsonElement card);
        TryGetObject(cardPlay, "target", out JsonElement target);
        TryGetObject(cardPlay, "resources", out JsonElement resources);
        TryGetObject(root, "combat", out JsonElement combat);

        string targetText = target.ValueKind == JsonValueKind.Object
            ? $"{JsonString(target, "name")} hp={JsonInt(target, "hp")}/{JsonInt(target, "max_hp")} block={JsonInt(target, "block")}"
            : "no target";

        return $"card play {action}: {JsonString(card, "id")} -> {targetText} result={JsonString(cardPlay, "result_pile")} energy_spent={JsonInt(resources, "energy_spent")} encounter={JsonString(combat, "encounter")} round={JsonInt(combat, "round")}";
    }

    private static string DescribeCardSelectionOffered(JsonElement root)
    {
        TryGetObject(root, "player", out JsonElement player);
        return $"card selection offered: id={JsonString(root, "selection_id")} kind={JsonString(root, "selection_kind")} player={JsonInt(player, "net_id")} candidates={JsonArrayLength(root, "candidates")} min={JsonInt(root, "min_select")} max={JsonInt(root, "max_select")}";
    }

    private static string DescribeCardSelectionSelected(JsonElement root)
    {
        TryGetObject(root, "player", out JsonElement player);
        return $"card selection selected: id={JsonString(root, "selection_id")} kind={JsonString(root, "selection_kind")} player={JsonInt(player, "net_id")} selected=[{DescribeSelectedCards(root)}] skipped={JsonBool(root, "skipped")}";
    }

    private static string DescribeDeckMutation(JsonElement root, string action)
    {
        TryGetObject(root, "card", out JsonElement card);
        TryGetObject(root, "local_player", out JsonElement player);
        return $"deck card {action}: {JsonString(card, "id")} deck_size={JsonArrayLength(player, "deck")} hp={JsonInt(player, "hp")}/{JsonInt(player, "max_hp")} gold={JsonInt(player, "gold")}";
    }

    private static string DescribeEventOptionsOffered(JsonElement root)
    {
        TryGetObject(root, "event", out JsonElement eventElement);
        TryGetObject(root, "player", out JsonElement player);
        string options = DescribeEventOptions(root, "options");
        return $"event options offered: event={JsonString(eventElement, "id")} player={JsonInt(player, "net_id")} options=[{options}]";
    }

    private static string DescribeEventOptionSelected(JsonElement root)
    {
        TryGetObject(root, "event", out JsonElement eventElement);
        TryGetObject(root, "player", out JsonElement player);
        TryGetObject(root, "option", out JsonElement option);
        return $"event option selected: event={JsonString(eventElement, "id")} player={JsonInt(player, "net_id")} index={JsonInt(root, "option_index")} key={JsonString(option, "text_key")} title={JsonString(option, "title")}";
    }

    private static string DescribePotionUseStarted(JsonElement root)
    {
        TryGetObject(root, "potion", out JsonElement potion);
        TryGetObject(root, "target", out JsonElement target);
        TryGetObject(root, "player_before", out JsonElement player);
        string targetText = target.ValueKind == JsonValueKind.Object ? $"{JsonString(target, "name")} hp={JsonInt(target, "hp")}/{JsonInt(target, "max_hp")}" : "no target";
        return $"potion use started: {JsonString(potion, "id")} slot={JsonInt(root, "slot_index")} target={targetText} hp={JsonInt(player, "hp")}/{JsonInt(player, "max_hp")}";
    }

    private static string DescribePotionUsed(JsonElement root)
    {
        TryGetObject(root, "potion", out JsonElement potion);
        TryGetObject(root, "target", out JsonElement target);
        TryGetObject(root, "player", out JsonElement player);
        string targetText = target.ValueKind == JsonValueKind.Object ? $"{JsonString(target, "name")} hp={JsonInt(target, "hp")}/{JsonInt(target, "max_hp")}" : "no target";
        return $"potion used: {JsonString(potion, "id")} target={targetText} hp={JsonInt(player, "hp")}/{JsonInt(player, "max_hp")} gold={JsonInt(player, "gold")}";
    }

    private static string DescribePotionDiscarded(JsonElement root)
    {
        TryGetObject(root, "potion", out JsonElement potion);
        TryGetObject(root, "player_before", out JsonElement player);
        return $"potion discarded: {JsonString(potion, "id")} slot={JsonInt(root, "slot_index")} hp={JsonInt(player, "hp")}/{JsonInt(player, "max_hp")} gold={JsonInt(player, "gold")}";
    }

    private static string DescribeShopItemsOffered(JsonElement root)
    {
        TryGetObject(root, "shop", out JsonElement shop);
        TryGetObject(root, "player", out JsonElement player);
        return $"shop items offered: cards={JsonArrayLength(shop, "cards")} relics={JsonArrayLength(shop, "relics")} potions={JsonArrayLength(shop, "potions")} removal={(shop.TryGetProperty("card_removal", out JsonElement removal) && removal.ValueKind == JsonValueKind.Object ? JsonInt(removal, "cost") : "none")} gold={JsonInt(player, "gold")}";
    }

    private static string DescribeShopItemPurchased(JsonElement root)
    {
        TryGetObject(root, "item", out JsonElement item);
        TryGetObject(root, "player", out JsonElement player);
        return $"shop item purchased: {DescribeShopEntry(item)} spent={JsonInt(root, "gold_spent")} gold={JsonInt(root, "gold_before")}->{JsonInt(root, "gold_after")} hp={JsonInt(player, "hp")}/{JsonInt(player, "max_hp")}";
    }

    private static string DescribeRunEnded(JsonElement root)
    {
        TryGetObject(root, "run", out JsonElement run);
        TryGetObject(root, "local_player", out JsonElement player);
        return $"run ended: victory={JsonBool(root, "victory")} abandoned={JsonBool(root, "abandoned")} floor={JsonInt(run, "total_floor")} hp={JsonInt(player, "hp")}/{JsonInt(player, "max_hp")} gold={JsonInt(player, "gold")}";
    }

    private static string DescribeRoomFromRun(JsonElement run)
    {
        if (!TryGetObject(run, "room", out JsonElement room))
        {
            return "none";
        }

        string model = JsonString(room, "model");
        string type = JsonString(room, "type");
        return string.IsNullOrWhiteSpace(model) ? type : model;
    }

    private static string DescribeEnemies(JsonElement combat)
    {
        if (!combat.TryGetProperty("enemies", out JsonElement enemies) || enemies.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        return string.Join(", ", enemies.EnumerateArray().Select(enemy =>
        {
            string name = JsonString(enemy, "name");
            string intent = JsonString(enemy, "intent");
            return $"{name} hp={JsonInt(enemy, "hp")}/{JsonInt(enemy, "max_hp")} block={JsonInt(enemy, "block")} intent={intent}";
        }));
    }

    private static string DescribeShopEntry(JsonElement item)
    {
        string kind = JsonString(item, "kind");
        string cost = JsonInt(item, "cost");
        return kind switch
        {
            "card" when TryGetObject(item, "card", out JsonElement card) => $"card {JsonString(card, "id")} cost={cost}",
            "relic" when TryGetObject(item, "relic", out JsonElement relic) => $"relic {JsonString(relic, "id")} cost={cost}",
            "potion" when TryGetObject(item, "potion", out JsonElement potion) => $"potion {JsonString(potion, "id")} cost={cost}",
            "card_removal" => $"card removal cost={cost}",
            _ => $"{kind} cost={cost}"
        };
    }

    private static string DescribeEventOptions(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement options) || options.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        return string.Join(", ", options.EnumerateArray().Select(option =>
        {
            string index = JsonInt(option, "index");
            string title = JsonString(option, "title");
            string key = JsonString(option, "text_key");
            string locked = JsonBool(option, "is_locked") == true ? " locked" : "";
            return $"{index}:{(string.IsNullOrWhiteSpace(title) ? key : title)}{locked}";
        }));
    }

    private static string DescribeSelectedCards(JsonElement root)
    {
        if (!root.TryGetProperty("selected", out JsonElement selected) || selected.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        return string.Join(", ", selected.EnumerateArray().Select(item =>
        {
            TryGetObject(item, "card", out JsonElement card);
            return $"{JsonInt(item, "index")}:{JsonString(card, "id")}";
        }));
    }

    private static string CompactSelectedCards(JsonElement root)
    {
        if (!root.TryGetProperty("selected", out JsonElement selected) || selected.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        return string.Join(",", selected.EnumerateArray().Select(item =>
        {
            TryGetObject(item, "card", out JsonElement card);
            return JsonString(card, "id");
        }).Where(id => !string.IsNullOrWhiteSpace(id)));
    }

    private static string JsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return "";
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? "",
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => ""
        };
    }

    private static string JsonInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind == JsonValueKind.Null)
        {
            return "null";
        }

        return property.ValueKind == JsonValueKind.Number ? property.GetRawText() : "";
    }

    private static bool? JsonBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static int JsonArrayLength(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.Array
            ? property.GetArrayLength()
            : 0;
    }

    private static List<string> JsonStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        return property.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "")
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    public static void WriteSafe(string type, Func<object?> payloadFactory, string? summary = null)
    {
        try
        {
            Write(type, payloadFactory(), summary);
        }
        catch (Exception ex)
        {
            Error($"Failed to build event '{type}': {ex}");
        }
    }

    private static long CountLines(string path)
    {
        return File.Exists(path) ? File.ReadLines(path).LongCount() : 0;
    }

    private static RewindDetection? BuildRewindDetection(string path, RunState loadedRunState)
    {
        List<StateSnapshot> snapshots = ReadStateSnapshots(path);
        StateSnapshot? previous = snapshots.LastOrDefault();
        if (previous == null)
        {
            return null;
        }

        StateSnapshot loaded = BuildSnapshot(loadedRunState);
        List<object> differences = BuildDifferences(previous, loaded);
        if (differences.Count == 0)
        {
            return null;
        }

        StateSnapshot? lastMatching = snapshots.LastOrDefault(snapshot => HasSameComparableState(snapshot, loaded));
        long? invalidatedFrom = lastMatching == null ? null : lastMatching.EventIndex + 1;
        return new RewindDetection(previous, lastMatching, invalidatedFrom, differences);
    }

    private static List<StateSnapshot> ReadStateSnapshots(string path)
    {
        var snapshots = new List<StateSnapshot>();
        if (!File.Exists(path))
        {
            return snapshots;
        }

        foreach (string line in File.ReadLines(path))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (TryBuildSnapshot(root, out StateSnapshot? snapshot))
                {
                    snapshots.Add(snapshot!);
                }
            }
            catch
            {
                // Keep scanning old logs even if one historical line is malformed.
            }
        }

        return snapshots;
    }

    private static bool TryBuildSnapshot(JsonElement root, out StateSnapshot? snapshot)
    {
        snapshot = null;
        if (!root.TryGetProperty("event_index", out JsonElement eventIndexElement) ||
            !eventIndexElement.TryGetInt64(out long eventIndex) ||
            !root.TryGetProperty("type", out JsonElement typeElement) ||
            !root.TryGetProperty("payload", out JsonElement payload))
        {
            return false;
        }

        if (!TryFindPlayerSnapshot(payload, out JsonElement player))
        {
            return false;
        }

        snapshot = new StateSnapshot(
            eventIndex,
            typeElement.GetString() ?? "",
            root.TryGetProperty("summary", out JsonElement summaryElement) ? summaryElement.GetString() : null,
            ReadActIndex(payload),
            ReadTotalFloor(payload),
            ReadRoomField(payload, "type"),
            ReadRoomField(payload, "model"),
            ReadInCombat(payload, player),
            ReadCombatField(payload, "encounter"),
            ReadCombatRound(payload),
            ReadNullableInt(player, "hp"),
            ReadNullableInt(player, "max_hp"),
            ReadNullableInt(player, "gold"),
            ReadStringArray(player, "deck", "id"),
            ReadStringArray(player, "relics"),
            ReadStringArray(player, "potions"));
        return true;
    }

    private static bool TryFindPlayerSnapshot(JsonElement payload, out JsonElement player)
    {
        if (TryGetObject(payload, "local_player", out player))
        {
            return true;
        }

        if (TryGetObject(payload, "player", out player))
        {
            return true;
        }

        if (TryGetObject(payload, "run", out JsonElement run) &&
            TryGetFirstArrayObject(run, "players", out player))
        {
            return true;
        }

        if (TryGetObject(payload, "combat", out JsonElement combat) &&
            TryGetFirstArrayObject(combat, "players", out player))
        {
            return true;
        }

        player = default;
        return false;
    }

    private static StateSnapshot BuildSnapshot(RunState runState)
    {
        Player? player = runState.Players.FirstOrDefault();
        if (player == null)
        {
            return new StateSnapshot(-1, "loaded_state", null, runState.CurrentActIndex, runState.TotalFloor, null, null, false, null, null, null, null, null, new List<string>(), new List<string>(), new List<string>());
        }

        bool inCombat = player.Creature.CombatState != null;

        return new StateSnapshot(
            -1,
            "loaded_state",
            null,
            runState.CurrentActIndex,
            runState.TotalFloor,
            runState.CurrentRoom?.RoomType.ToString(),
            runState.CurrentRoom?.ModelId?.ToString(),
            inCombat,
            player.Creature.CombatState?.Encounter?.Id.ToString(),
            player.Creature.CombatState?.RoundNumber,
            player.Creature.CurrentHp,
            player.Creature.MaxHp,
            player.Gold,
            player.Deck.Cards.Select(BuildCardSignature).ToList(),
            player.Relics.Select(relic => relic.Id.ToString()).ToList(),
            player.PotionSlots.Select(potion => potion?.Id.ToString() ?? "").ToList());
    }

    private static int? ReadActIndex(JsonElement payload)
    {
        if (TryGetObject(payload, "run", out JsonElement run))
        {
            return ReadNullableInt(run, "act_index");
        }

        if (TryGetObject(payload, "combat", out JsonElement combat) &&
            TryGetObject(combat, "run", out JsonElement combatRun))
        {
            return ReadNullableInt(combatRun, "act_index");
        }

        return null;
    }

    private static int? ReadTotalFloor(JsonElement payload)
    {
        if (TryGetObject(payload, "run", out JsonElement run))
        {
            return ReadNullableInt(run, "total_floor");
        }

        if (TryGetObject(payload, "combat", out JsonElement combat) &&
            TryGetObject(combat, "run", out JsonElement combatRun))
        {
            return ReadNullableInt(combatRun, "total_floor");
        }

        return null;
    }

    private static string? ReadRoomField(JsonElement payload, string field)
    {
        if (TryGetObject(payload, "run", out JsonElement run) &&
            TryGetObject(run, "room", out JsonElement runRoom))
        {
            return ReadNullableString(runRoom, field);
        }

        if (TryGetObject(payload, "room", out JsonElement room))
        {
            return ReadNullableString(room, field);
        }

        if (TryGetObject(payload, "combat", out JsonElement combat) &&
            TryGetObject(combat, "run", out JsonElement combatRun) &&
            TryGetObject(combatRun, "room", out JsonElement combatRoom))
        {
            return ReadNullableString(combatRoom, field);
        }

        return null;
    }

    private static bool? ReadInCombat(JsonElement payload, JsonElement player)
    {
        if (player.TryGetProperty("in_combat", out JsonElement inCombat))
        {
            return inCombat.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }

        return TryGetObject(payload, "combat", out _) ? true : null;
    }

    private static string? ReadCombatField(JsonElement payload, string field)
    {
        return TryGetObject(payload, "combat", out JsonElement combat)
            ? ReadNullableString(combat, field)
            : null;
    }

    private static int? ReadCombatRound(JsonElement payload)
    {
        return TryGetObject(payload, "combat", out JsonElement combat)
            ? ReadNullableInt(combat, "round")
            : null;
    }

    private static List<object> BuildDifferences(StateSnapshot previous, StateSnapshot loaded)
    {
        var differences = new List<object>();
        AddScalarDifference(differences, "act_index", previous.ActIndex, loaded.ActIndex);
        AddScalarDifference(differences, "total_floor", previous.TotalFloor, loaded.TotalFloor);
        AddStringDifference(differences, "room_type", previous.RoomType, loaded.RoomType);
        AddStringDifference(differences, "room_model", previous.RoomModel, loaded.RoomModel);
        AddBoolDifference(differences, "in_combat", previous.InCombat, loaded.InCombat);
        AddStringDifference(differences, "combat_encounter", previous.CombatEncounter, loaded.CombatEncounter);
        AddScalarDifference(differences, "combat_round", previous.CombatRound, loaded.CombatRound);
        AddScalarDifference(differences, "hp", previous.Hp, loaded.Hp);
        AddScalarDifference(differences, "max_hp", previous.MaxHp, loaded.MaxHp);
        AddScalarDifference(differences, "gold", previous.Gold, loaded.Gold);
        AddListDifference(differences, "deck", previous.Deck, loaded.Deck);
        AddListDifference(differences, "relics", previous.Relics, loaded.Relics);
        AddListDifference(differences, "potions", previous.Potions, loaded.Potions);
        return differences;
    }

    private static void AddScalarDifference(List<object> differences, string field, int? previous, int? loaded)
    {
        if (previous == loaded)
        {
            return;
        }

        differences.Add(new
        {
            field,
            previous,
            loaded
        });
    }

    private static void AddStringDifference(List<object> differences, string field, string? previous, string? loaded)
    {
        if (previous == loaded)
        {
            return;
        }

        differences.Add(new
        {
            field,
            previous,
            loaded
        });
    }

    private static void AddBoolDifference(List<object> differences, string field, bool? previous, bool? loaded)
    {
        if (previous == loaded)
        {
            return;
        }

        differences.Add(new
        {
            field,
            previous,
            loaded
        });
    }

    private static void AddListDifference(List<object> differences, string field, IReadOnlyList<string> previous, IReadOnlyList<string> loaded)
    {
        if (previous.SequenceEqual(loaded))
        {
            return;
        }

        differences.Add(new
        {
            field,
            previous,
            loaded,
            removed_by_rewind = MultisetExcept(previous, loaded),
            added_by_rewind = MultisetExcept(loaded, previous)
        });
    }

    private static bool HasSameComparableState(StateSnapshot left, StateSnapshot right)
    {
        return left.Hp == right.Hp &&
               left.MaxHp == right.MaxHp &&
               left.Gold == right.Gold &&
               left.Deck.SequenceEqual(right.Deck) &&
               left.Relics.SequenceEqual(right.Relics) &&
               left.Potions.SequenceEqual(right.Potions) &&
               LocationMatches(left, right);
    }

    private static bool LocationMatches(StateSnapshot left, StateSnapshot right)
    {
        if (left.ActIndex != right.ActIndex || left.TotalFloor != right.TotalFloor)
        {
            return false;
        }

        bool roomMatches = string.IsNullOrEmpty(right.RoomType) ||
                           (left.RoomType == right.RoomType && left.RoomModel == right.RoomModel);
        if (!roomMatches)
        {
            return false;
        }

        if (right.InCombat != true)
        {
            return true;
        }

        return left.InCombat == right.InCombat &&
               left.CombatEncounter == right.CombatEncounter &&
               left.CombatRound == right.CombatRound;
    }

    private static List<string> MultisetExcept(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var counts = new Dictionary<string, int>();
        foreach (string value in right)
        {
            counts[value] = counts.GetValueOrDefault(value) + 1;
        }

        var result = new List<string>();
        foreach (string value in left)
        {
            int count = counts.GetValueOrDefault(value);
            if (count > 0)
            {
                counts[value] = count - 1;
            }
            else
            {
                result.Add(value);
            }
        }

        return result;
    }

    private static bool TryGetObject(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetFirstArrayObject(JsonElement element, string propertyName, out JsonElement value)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            value = default;
            return false;
        }

        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                value = item;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static int? ReadNullableInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out int value))
        {
            return null;
        }

        return value;
    }

    private static string? ReadNullableString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static List<string> ReadStringArray(JsonElement element, string propertyName, string? nestedPropertyName = null)
    {
        var values = new List<string>();
        if (!element.TryGetProperty(propertyName, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return values;
        }

        foreach (JsonElement item in array.EnumerateArray())
        {
            if (nestedPropertyName != null && item.ValueKind == JsonValueKind.Object)
            {
                values.Add(BuildCardSignature(item));
                continue;
            }

            values.Add(item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "");
        }

        return values;
    }

    private static void EnsureDeckMutationCacheLoaded()
    {
        if (_logPath == null || _deckMutationCachePath == _logPath)
        {
            return;
        }

        LoadDeckMutationCache(_logPath);
    }

    private static void LoadDeckMutationCache(string path)
    {
        DeckMutationKeys.Clear();
        _deckMutationCachePath = path;

        if (!File.Exists(path))
        {
            return;
        }

        foreach (string line in File.ReadLines(path))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (!root.TryGetProperty("type", out JsonElement typeElement))
                {
                    continue;
                }

                string? type = typeElement.GetString();
                if (type is not ("deck_card_added" or "deck_card_removed"))
                {
                    continue;
                }

                if (!root.TryGetProperty("payload", out JsonElement payload))
                {
                    continue;
                }

                if (!TryReadUInt64(payload, "player", "net_id", out ulong netId) &&
                    !TryReadUInt64(payload, "local_player", "net_id", out netId))
                {
                    continue;
                }

                if (!payload.TryGetProperty("card", out JsonElement card) ||
                    !payload.TryGetProperty("deck", out JsonElement deck))
                {
                    continue;
                }

                DeckMutationKeys.Add(BuildDeckMutationKey(type, netId, BuildCardSignature(card), BuildDeckSignature(deck)));
            }
            catch
            {
                // A malformed old line should not disable logging for the rest of the run.
            }
        }
    }

    private static bool TryReadUInt64(JsonElement payload, string objectName, string propertyName, out ulong value)
    {
        value = 0;
        if (!payload.TryGetProperty(objectName, out JsonElement obj) ||
            obj.ValueKind != JsonValueKind.Object ||
            !obj.TryGetProperty(propertyName, out JsonElement property))
        {
            return false;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetUInt64(out value);
    }

    private static string BuildDeckMutationKey(string type, ulong ownerNetId, string cardSignature, string deckSignature)
    {
        return $"{type}|{ownerNetId}|{cardSignature}|{deckSignature}";
    }

    private static string BuildDeckSignature(IEnumerable<CardModel> cards)
    {
        return string.Join(";", cards.Select(BuildCardSignature));
    }

    private static string BuildDeckSignature(JsonElement deck)
    {
        if (deck.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Join(";", deck.EnumerateArray().Select(BuildCardSignature));
    }

    private static string BuildCardSignature(CardModel card)
    {
        return string.Join("|",
            card.Id.ToString(),
            card.CurrentUpgradeLevel.ToString(),
            card.Enchantment?.Id.ToString() ?? "",
            card.Affliction?.Id.ToString() ?? "");
    }

    private static string BuildCardSignature(JsonElement card)
    {
        return string.Join("|",
            ReadString(card, "id"),
            ReadString(card, "upgrade_level"),
            ReadString(card, "enchantment"),
            ReadString(card, "affliction"));
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private sealed record StateSnapshot(
        long EventIndex,
        string EventType,
        string? Summary,
        int? ActIndex,
        int? TotalFloor,
        string? RoomType,
        string? RoomModel,
        bool? InCombat,
        string? CombatEncounter,
        int? CombatRound,
        int? Hp,
        int? MaxHp,
        int? Gold,
        List<string> Deck,
        List<string> Relics,
        List<string> Potions);

    private sealed record RewindDetection(
        StateSnapshot Previous,
        StateSnapshot? LastMatching,
        long? InvalidatedFromEventIndex,
        List<object> Differences);
}
