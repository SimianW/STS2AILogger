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
        EventLogger.WriteSafe("run_started", () => new
        {
            run = GameSnapshots.Run(state),
            local_player = GameSnapshots.Player(LocalContext.GetMe(state))
        });
    }

    private static void OnRoomEntered()
    {
        EventLogger.WriteSafe("room_entered", () =>
        {
            RunState? state = RunManager.Instance.DebugOnlyGetState();
            return new
            {
                run = GameSnapshots.Run(state),
                local_player = GameSnapshots.Player(LocalContext.GetMe(state))
            };
        });
    }

    private static void OnCombatSetUp(CombatState state)
    {
        EventLogger.WriteSafe("combat_setup", () => new
        {
            combat = GameSnapshots.Combat(state),
            local_player = GameSnapshots.Player(LocalContext.GetMe(state))
        });
    }

    private static void OnTurnStarted(CombatState state)
    {
        EventLogger.WriteSafe("turn_started", () => new
        {
            combat = GameSnapshots.Combat(state),
            local_player = GameSnapshots.Player(LocalContext.GetMe(state))
        });
    }

    private static void OnTurnEnded(CombatState state)
    {
        EventLogger.WriteSafe("turn_ended", () => new
        {
            combat = GameSnapshots.Combat(state),
            local_player = GameSnapshots.Player(LocalContext.GetMe(state))
        });
    }

    private static void OnCombatEnded(CombatRoom room)
    {
        EventLogger.WriteSafe("combat_ended", () =>
        {
            RunState? state = RunManager.Instance.DebugOnlyGetState();
            return new
            {
                room = GameSnapshots.Room(room),
                run = GameSnapshots.Run(state),
                local_player = GameSnapshots.Player(LocalContext.GetMe(state))
            };
        });
    }

    private static void OnPlayerEndedTurn(Player player, bool canBackOut)
    {
        EventLogger.WriteSafe("player_ended_turn", () =>
        {
            CombatState? state = CombatManager.Instance.DebugOnlyGetState();
            return new
            {
                can_back_out = canBackOut,
                player = GameSnapshots.Player(player),
                combat = GameSnapshots.Combat(state)
            };
        });
    }
}
