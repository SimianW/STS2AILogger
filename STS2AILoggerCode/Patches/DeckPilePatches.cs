using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2AILogger.STS2AILoggerCode.Logging;

namespace STS2AILogger.Patches;

[HarmonyPatch(typeof(CardPile), nameof(CardPile.AddInternal))]
public static class DeckCardAddedPatch
{
    public static void Postfix(CardPile __instance, CardModel card, bool silent)
    {
        Player? owner = card.Owner;
        if (__instance.Type != PileType.Deck || owner == null)
        {
            return;
        }

        EventLogger.SetRunContext(owner.RunState as RunState);
        if (!EventLogger.TryRememberDeckMutation("deck_card_added", owner, card, __instance.Cards))
        {
            return;
        }

        EventLogger.WriteSafe("deck_card_added", () =>
        {
            return new
            {
                silent,
                card = GameSnapshots.Card(card),
                deck = GameSnapshots.Cards(__instance.Cards),
                player = GameSnapshots.Player(owner),
                local_player = GameSnapshots.Player(LocalContext.GetMe(owner.RunState))
            };
        }, $"deck card added: {card.Id}");
    }
}

[HarmonyPatch(typeof(CardPile), nameof(CardPile.RemoveInternal))]
public static class DeckCardRemovedPatch
{
    public static void Postfix(CardPile __instance, CardModel card, bool silent)
    {
        Player? owner = card.Owner;
        if (__instance.Type != PileType.Deck || owner == null)
        {
            return;
        }

        EventLogger.SetRunContext(owner.RunState as RunState);
        if (!EventLogger.TryRememberDeckMutation("deck_card_removed", owner, card, __instance.Cards))
        {
            return;
        }

        EventLogger.WriteSafe("deck_card_removed", () =>
        {
            return new
            {
                silent,
                card = GameSnapshots.Card(card),
                deck = GameSnapshots.Cards(__instance.Cards),
                player = GameSnapshots.Player(owner),
                local_player = GameSnapshots.Player(LocalContext.GetMe(owner.RunState))
            };
        }, $"deck card removed: {card.Id}");
    }
}
