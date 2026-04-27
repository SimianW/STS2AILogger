using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Runs;
using STS2AILogger.STS2AILoggerCode.Logging;

namespace STS2AILogger.Patches;

[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.CardPlayStarted))]
public static class CardPlayStartedPatch
{
    public static void Prefix(CombatState combatState, CardPlay cardPlay)
    {
        EventLogger.WriteSafe("card_play_started", () =>
        {
            EventLogger.SetRunContext(combatState.RunState as RunState);
            return new
            {
                card_play = GameSnapshots.CardPlay(cardPlay),
                combat = GameSnapshots.Combat(combatState)
            };
        }, $"card play started: {cardPlay.Card.Id}");
    }
}

[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.CardPlayFinished))]
public static class CardPlayFinishedPatch
{
    public static void Postfix(CombatState combatState, CardPlay cardPlay)
    {
        EventLogger.WriteSafe("card_play_finished", () =>
        {
            EventLogger.SetRunContext(combatState.RunState as RunState);
            return new
            {
                card_play = GameSnapshots.CardPlay(cardPlay),
                combat = GameSnapshots.Combat(combatState)
            };
        }, $"card play finished: {cardPlay.Card.Id}");
    }
}
