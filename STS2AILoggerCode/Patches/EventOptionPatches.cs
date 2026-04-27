using System;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2AILogger.STS2AILoggerCode.Logging;

namespace STS2AILogger.Patches;

[HarmonyPatch(typeof(EventModel), "SetEventState")]
public static class EventOptionsOfferedPatch
{
    public static void Postfix(EventModel __instance)
    {
        try
        {
            if (__instance.CurrentOptions.Count == 0)
            {
                return;
            }

            Player? player = __instance.Owner;
            RunState? runState = player?.RunState as RunState;
            EventLogger.SetRunContext(runState);
            EventLogger.Write("event_options_offered", new
            {
                @event = SnapshotEvent(__instance),
                player = GameSnapshots.Player(player),
                options = __instance.CurrentOptions.Select(SnapshotOption).ToList(),
                run = GameSnapshots.Run(runState)
            }, $"event options offered: {__instance.Id}");
        }
        catch (Exception ex)
        {
            EventLogger.Error($"Failed to log event options: {ex}");
        }
    }

    internal static object SnapshotEvent(EventModel eventModel)
    {
        return new
        {
            id = eventModel.Id.ToString(),
            title = SafeRawText(eventModel.Title),
            description = eventModel.Description == null ? null : SafeRawText(eventModel.Description),
            is_shared = eventModel.IsShared,
            is_finished = eventModel.IsFinished
        };
    }

    internal static object SnapshotOption(EventOption option, int index)
    {
        return new
        {
            index,
            text_key = option.TextKey,
            title = SafeRawText(option.Title),
            description = SafeRawText(option.Description),
            is_locked = option.IsLocked,
            is_proceed = option.IsProceed,
            was_chosen = option.WasChosen,
            relic = option.Relic?.Id.ToString(),
            should_save_choice_to_history = option.ShouldSaveChoiceToHistory,
            will_kill_player = option.WillKillPlayer != null
        };
    }

    private static string? SafeRawText(LocString? locString)
    {
        try
        {
            return locString?.GetRawText();
        }
        catch
        {
            return locString?.ToString();
        }
    }
}

[HarmonyPatch(typeof(EventSynchronizer), "ChooseOptionForEvent")]
public static class EventOptionSelectedPatch
{
    public static void Prefix(EventSynchronizer __instance, Player player, int optionIndex)
    {
        try
        {
            EventModel eventModel = __instance.GetEventForPlayer(player);
            EventOption? option = optionIndex >= 0 && optionIndex < eventModel.CurrentOptions.Count
                ? eventModel.CurrentOptions[optionIndex]
                : null;
            RunState? runState = player.RunState as RunState;

            EventLogger.SetRunContext(runState);
            EventLogger.Write("event_option_selected", new
            {
                @event = EventOptionsOfferedPatch.SnapshotEvent(eventModel),
                player = GameSnapshots.Player(player),
                option_index = optionIndex,
                option = option == null ? null : EventOptionsOfferedPatch.SnapshotOption(option, optionIndex),
                options_before_choice = eventModel.CurrentOptions.Select(EventOptionsOfferedPatch.SnapshotOption).ToList(),
                run = GameSnapshots.Run(runState)
            }, $"event option selected: {eventModel.Id} option={optionIndex}");
        }
        catch (Exception ex)
        {
            EventLogger.Error($"Failed to log selected event option: {ex}");
        }
    }
}
