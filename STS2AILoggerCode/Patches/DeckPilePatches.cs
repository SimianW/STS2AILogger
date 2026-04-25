using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using STS2AILogger.STS2AILoggerCode.Logging;

namespace STS2AILogger.Patches;

[HarmonyPatch(typeof(CardPile), nameof(CardPile.AddInternal))]
public static class DeckCardAddedPatch
{
    public static void Postfix(CardPile __instance, CardModel card, bool silent)
    {
        if (__instance.Type != PileType.Deck)
        {
            return;
        }

        EventLogger.WriteSafe("deck_card_added", () => new
        {
            silent,
            card = GameSnapshots.Card(card),
            deck = GameSnapshots.Cards(__instance.Cards),
            player = GameSnapshots.Player(card.Owner),
            local_player = GameSnapshots.Player(LocalContext.GetMe(card.Owner?.RunState))
        });
    }
}

[HarmonyPatch(typeof(CardPile), nameof(CardPile.RemoveInternal))]
public static class DeckCardRemovedPatch
{
    public static void Postfix(CardPile __instance, CardModel card, bool silent)
    {
        if (__instance.Type != PileType.Deck)
        {
            return;
        }

        EventLogger.WriteSafe("deck_card_removed", () => new
        {
            silent,
            card = GameSnapshots.Card(card),
            deck = GameSnapshots.Cards(__instance.Cards),
            player = GameSnapshots.Player(card.Owner),
            local_player = GameSnapshots.Player(LocalContext.GetMe(card.Owner?.RunState))
        });
    }
}
