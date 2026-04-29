using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace STS2AILogger.STS2AILoggerCode.Logging;

public sealed record TreasureSelectionLogState(
    string SelectionId,
    IReadOnlyList<RelicModel> Candidates);

public static class TreasureSelectionLogContext
{
    private static long _nextSelectionId;
    private static TreasureSelectionLogState? _pending;
    private static string? _lastImplicitNoCandidatesRunKey;
    private static int? _lastImplicitNoCandidatesFloor;

    public static void LogOffered(TreasureRoomRelicSynchronizer synchronizer, RunState? runState)
    {
        if (runState == null)
        {
            return;
        }

        IReadOnlyList<RelicModel> candidates = synchronizer.CurrentRelics?.ToList() ?? new List<RelicModel>();
        string selectionId = $"treasure-choice-{Interlocked.Increment(ref _nextSelectionId)}";
        _pending = new TreasureSelectionLogState(selectionId, candidates);
        _lastImplicitNoCandidatesRunKey = null;
        _lastImplicitNoCandidatesFloor = null;

        EventLogger.SetRunContext(runState);
        EventLogger.Write("treasure_relics_offered", new
        {
            selection_id = selectionId,
            selection_kind = "treasure_relic",
            can_skip = true,
            skip_available_after_seconds = 2.5,
            candidates = CandidateSnapshots(candidates),
            run = GameSnapshots.Run(runState),
            player = GameSnapshots.Player(LocalContext.GetMe(runState))
        }, "treasure relics offered");
    }

    public static void LogPicked(TreasureRoomRelicSynchronizer synchronizer, int? index, RunState? runState)
    {
        if (runState == null)
        {
            return;
        }

        TreasureSelectionLogState? state = _pending;
        if (state == null)
        {
            EventLogger.Debug($"Ignoring treasure selection result without pending offer at floor {runState.TotalFloor}: {(index.HasValue ? $"index={index.Value}" : "skip")}");
            return;
        }

        _pending = null;

        EventLogger.SetRunContext(runState);
        if (index.HasValue)
        {
            EventLogger.Write("treasure_relic_selected", new
            {
                selection_id = state.SelectionId,
                selection_kind = "treasure_relic",
                selected = SelectedSnapshot(state.Candidates, index.Value),
                skipped = false,
                selection_result = "selected",
                candidates = CandidateSnapshots(state.Candidates),
                run = GameSnapshots.Run(runState),
                player = GameSnapshots.Player(LocalContext.GetMe(runState))
            }, "treasure relic selected");
            return;
        }

        EventLogger.Write("treasure_relic_skipped", new
        {
            selection_id = state.SelectionId,
            selection_kind = "treasure_relic",
            selected = new List<object>(),
            skipped = true,
            selection_result = "skipped",
            candidates = CandidateSnapshots(state.Candidates),
            run = GameSnapshots.Run(runState),
            player = GameSnapshots.Player(LocalContext.GetMe(runState))
        }, "treasure relic skipped");
    }

    public static void LogNoRelics(TreasureRoomRelicSynchronizer synchronizer, RunState? runState)
    {
        if (runState == null)
        {
            return;
        }

        TreasureSelectionLogState? state = _pending;
        if (state == null)
        {
            if (IsDuplicateImplicitNoCandidates(runState))
            {
                EventLogger.Debug($"Ignoring duplicate empty treasure completion at floor {runState.TotalFloor}");
                return;
            }

            IReadOnlyList<RelicModel> candidates = synchronizer.CurrentRelics?.ToList() ?? new List<RelicModel>();
            state = new TreasureSelectionLogState(
                $"treasure-choice-{Interlocked.Increment(ref _nextSelectionId)}",
                candidates);
            RememberImplicitNoCandidates(runState);
        }

        _pending = null;

        EventLogger.SetRunContext(runState);
        EventLogger.Write("treasure_relic_skipped", new
        {
            selection_id = state.SelectionId,
            selection_kind = "treasure_relic",
            selected = new List<object>(),
            skipped = false,
            selection_result = "no_candidates",
            candidates = CandidateSnapshots(state.Candidates),
            run = GameSnapshots.Run(runState),
            player = GameSnapshots.Player(LocalContext.GetMe(runState))
        }, "treasure relic no candidates");
    }

    private static List<object> CandidateSnapshots(IReadOnlyList<RelicModel> candidates)
    {
        return candidates.Select((relic, index) => new
        {
            index,
            relic = GameSnapshots.Model(relic)
        }).Cast<object>().ToList();
    }

    private static List<object> SelectedSnapshot(IReadOnlyList<RelicModel> candidates, int index)
    {
        if (index < 0 || index >= candidates.Count)
        {
            return new List<object>();
        }

        return new List<object>
        {
            new
            {
                index,
                relic = GameSnapshots.Model(candidates[index])
            }
        };
    }

    private static bool IsDuplicateImplicitNoCandidates(RunState runState)
    {
        return _lastImplicitNoCandidatesRunKey == RunLogContext.BuildRunKey(runState) &&
               _lastImplicitNoCandidatesFloor == runState.TotalFloor;
    }

    private static void RememberImplicitNoCandidates(RunState runState)
    {
        _lastImplicitNoCandidatesRunKey = RunLogContext.BuildRunKey(runState);
        _lastImplicitNoCandidatesFloor = runState.TotalFloor;
    }
}
