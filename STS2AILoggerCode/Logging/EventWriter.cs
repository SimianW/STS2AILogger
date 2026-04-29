using System;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace STS2AILogger.STS2AILoggerCode.Logging;

public static class EventWriter
{
    public static void WriteRunEvent(string type, RunState? runState, Func<object?> payloadFactory, string? summary = null)
    {
        EventLogger.WriteSafe(type, () =>
        {
            EventLogger.SetRunContext(runState);
            return payloadFactory();
        }, summary);
    }

    public static void WriteCombatEvent(string type, CombatState? combatState, Func<object?> payloadFactory, string? summary = null)
    {
        EventLogger.WriteSafe(type, () =>
        {
            EventLogger.SetRunContext(combatState?.RunState as RunState);
            return payloadFactory();
        }, summary);
    }

    public static void WriteCardSelectionOffered(CardSelectionLogState state)
    {
        EventLogger.WriteSafe("card_selection_offered", () =>
        {
            EventLogger.SetRunContext(state.Player?.RunState as RunState);
            return SelectionSnapshots.Offered(state);
        }, $"card selection offered: {state.SelectionKind}");
    }

    public static void WriteCardSelectionSelected(CardSelectionLogState state, object selectedCards)
    {
        EventLogger.WriteSafe("card_selection_selected", () =>
        {
            EventLogger.SetRunContext(state.Player?.RunState as RunState);
            return SelectionSnapshots.Selected(state, selectedCards);
        }, $"card selection selected: {state.SelectionKind}");
    }

    public static object? LocalPlayerSnapshot(RunState? runState)
    {
        return GameSnapshots.Player(runState == null ? null : LocalContext.GetMe(runState));
    }
}
