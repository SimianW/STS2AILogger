using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2AILogger.STS2AILoggerCode.Logging;

namespace STS2AILogger.Patches;

[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.CardPlayStarted))]
public static class CardPlayStartedPatch
{
    public static void Prefix(CombatState combatState, CardPlay cardPlay)
    {
        string correlationId = CardPlayLogContext.GetOrCreateCorrelationId(cardPlay);
        EventLogger.WriteSafe("card_play_started", () =>
        {
            EventLogger.SetRunContext(combatState.RunState as RunState);
            return new
            {
                card_play = GameSnapshots.CardPlay(cardPlay, correlationId),
                available_hand_before_play = SnapshotAvailableHandBeforePlay(cardPlay),
                combat = GameSnapshots.Combat(combatState)
            };
        }, $"card play started: {cardPlay.Card.Id}");
    }

    private static object SnapshotAvailableHandBeforePlay(CardPlay cardPlay)
    {
        Player? owner = cardPlay.Card.Owner;
        List<CardModel> cards = owner?.PlayerCombatState?.Hand.Cards.ToList() ?? new List<CardModel>();
        if (!cards.Contains(cardPlay.Card))
        {
            cards.Insert(0, cardPlay.Card);
        }

        return GameSnapshots.Cards(cards);
    }
}

[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.CardPlayFinished))]
public static class CardPlayFinishedPatch
{
    public static void Postfix(CombatState combatState, CardPlay cardPlay)
    {
        string correlationId = CardPlayLogContext.GetOrCreateCorrelationId(cardPlay);
        EventLogger.WriteSafe("card_play_finished", () =>
        {
            EventLogger.SetRunContext(combatState.RunState as RunState);
            return new
            {
                card_play = GameSnapshots.CardPlay(cardPlay, correlationId),
                combat = GameSnapshots.Combat(combatState)
            };
        }, $"card play finished: {cardPlay.Card.Id}");
        CardPlayLogContext.Release(cardPlay);
    }
}
