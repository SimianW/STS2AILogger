using System;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using STS2AILogger.STS2AILoggerCode.Logging;

namespace STS2AILogger.STS2AILoggerCode;

public static class RuntimeEventHooks
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        RunManager.Instance.RunStarted += OnRunStarted;
        RunManager.Instance.RoomEntered += OnRoomEntered;

        CombatManager.Instance.CombatSetUp += OnCombatSetUp;
        CombatManager.Instance.TurnStarted += OnTurnStarted;
        CombatManager.Instance.TurnEnded += OnTurnEnded;
        CombatManager.Instance.CombatEnded += OnCombatEnded;
        CombatManager.Instance.PlayerEndedTurn += OnPlayerEndedTurn;

        EventLogger.Info("Runtime event hooks installed");
    }

    private static void OnRunStarted(RunState state)
    {
        EventLogger.SetRunContext(state);
        bool isLoadedRun = state.TotalFloor > 0 || EventLogger.CurrentRunHasPriorEvents;
        string eventType = isLoadedRun ? "run_loaded" : "run_started";
        string summary = isLoadedRun ? "run loaded" : "run started";
        long eventIndex = EventLogger.NextEventIndex;
        if (isLoadedRun)
        {
            EventLogger.PrepareStateRewindDetection(state);
        }

        EventLogger.WriteSafe(eventType, () =>
        {
            return new
            {
                run = GameSnapshots.Run(state),
                local_player = GameSnapshots.Player(LocalContext.GetMe(state))
            };
        }, summary);

        EventLogger.WritePendingStateRewindIfDetected(eventIndex);
    }

    private static void OnRoomEntered()
    {
        EventLogger.WriteSafe("room_entered", () =>
        {
            RunState? state = RunManager.Instance.DebugOnlyGetState();
            EventLogger.SetRunContext(state);
            return new
            {
                run = GameSnapshots.Run(state),
                local_player = GameSnapshots.Player(LocalContext.GetMe(state))
            };
        }, "room entered");
    }

    private static void OnCombatSetUp(CombatState state)
    {
        EventLogger.WriteSafe("combat_setup", () =>
        {
            EventLogger.SetRunContext(state.RunState as RunState);
            return new
            {
                combat = GameSnapshots.Combat(state),
                local_player = GameSnapshots.Player(LocalContext.GetMe(state))
            };
        }, $"combat setup: {state.Encounter?.Id}");
    }

    private static void OnTurnStarted(CombatState state)
    {
        EventLogger.WriteSafe("turn_started", () =>
        {
            EventLogger.SetRunContext(state.RunState as RunState);
            return new
            {
                combat = GameSnapshots.Combat(state),
                local_player = GameSnapshots.Player(LocalContext.GetMe(state))
            };
        }, $"turn started: round {state.RoundNumber} {state.CurrentSide}");
    }

    private static void OnTurnEnded(CombatState state)
    {
        EventLogger.WriteSafe("turn_ended", () =>
        {
            EventLogger.SetRunContext(state.RunState as RunState);
            return new
            {
                combat = GameSnapshots.Combat(state),
                local_player = GameSnapshots.Player(LocalContext.GetMe(state))
            };
        }, $"turn ended: round {state.RoundNumber} {state.CurrentSide}");
    }

    private static void OnCombatEnded(CombatRoom room)
    {
        EventLogger.WriteSafe("combat_ended", () =>
        {
            RunState? state = RunManager.Instance.DebugOnlyGetState();
            EventLogger.SetRunContext(state);
            return new
            {
                room = GameSnapshots.Room(room),
                run = GameSnapshots.Run(state),
                local_player = GameSnapshots.Player(LocalContext.GetMe(state))
            };
        }, "combat ended");
    }

    private static void OnPlayerEndedTurn(Player player, bool canBackOut)
    {
        EventLogger.WriteSafe("player_ended_turn", () =>
        {
            CombatState? state = CombatManager.Instance.DebugOnlyGetState();
            EventLogger.SetRunContext(state?.RunState as RunState);
            return new
            {
                can_back_out = canBackOut,
                player = GameSnapshots.Player(player),
                combat = GameSnapshots.Combat(state)
            };
        }, $"player ended turn: {player.NetId}");
    }
}
