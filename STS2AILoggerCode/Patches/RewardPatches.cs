using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using STS2AILogger.STS2AILoggerCode.Logging;

namespace STS2AILogger.Patches;

[HarmonyPatch]
public static class CardRewardSelectionPatch
{
    public static System.Reflection.MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(CardReward), "OnSelect");
    }

    public static void Prefix(CardReward __instance, out CardRewardLogState __state)
    {
        List<CardModel> candidates = ReadCards(__instance);
        Dictionary<string, int> beforeDeck = CountCards(__instance.Player.Deck.Cards);
        CardSelectionLogState selection = CardSelectionPatchHelpers.Offer(
            "card_reward",
            __instance.Player,
            candidates,
            null,
            __instance.CanSkip,
            false,
            null);
        __state = new CardRewardLogState(selection, beforeDeck);
    }

    public static void Postfix(ref Task<bool> __result, CardRewardLogState __state)
    {
        __result = LogSelectedReward(__result, __state);
    }

    private static async Task<bool> LogSelectedReward(Task<bool> original, CardRewardLogState state)
    {
        bool result = await original;
        List<CardModel> selected = state.Selection.Player == null
            ? new List<CardModel>()
            : FindNewDeckCards(state.Selection.Candidates, state.Selection.Player.Deck.Cards, state.BeforeDeck);
        EventWriter.WriteCardSelectionSelected(state.Selection, selected);
        return result;
    }

    private static List<CardModel> ReadCards(CardReward reward)
    {
        object? value = AccessTools.Field(typeof(CardReward), "_cards")?.GetValue(reward);
        return value is IEnumerable<CardCreationResult> cards
            ? cards.Select(card => card.Card).Where(card => card != null).ToList()
            : new List<CardModel>();
    }

    private static List<CardModel> FindNewDeckCards(IReadOnlyList<CardModel> candidates, IEnumerable<CardModel> deck, Dictionary<string, int> beforeDeck)
    {
        var remainingBefore = new Dictionary<string, int>(beforeDeck);
        var selected = new List<CardModel>();
        HashSet<string> candidateIds = candidates.Select(Signature).ToHashSet();
        foreach (CardModel card in deck)
        {
            string signature = Signature(card);
            int beforeCount = remainingBefore.GetValueOrDefault(signature);
            if (beforeCount > 0)
            {
                remainingBefore[signature] = beforeCount - 1;
                continue;
            }

            if (candidateIds.Contains(signature))
            {
                selected.Add(card);
            }
        }

        return selected;
    }

    private static Dictionary<string, int> CountCards(IEnumerable<CardModel> cards)
    {
        var counts = new Dictionary<string, int>();
        foreach (CardModel card in cards)
        {
            string signature = Signature(card);
            counts[signature] = counts.GetValueOrDefault(signature) + 1;
        }

        return counts;
    }

    private static string Signature(CardModel card)
    {
        return string.Join("|",
            card.Id.ToString(),
            card.CurrentUpgradeLevel.ToString(),
            card.Enchantment?.Id.ToString() ?? "",
            card.Affliction?.Id.ToString() ?? "");
    }
}

public sealed record CardRewardLogState(CardSelectionLogState Selection, Dictionary<string, int> BeforeDeck);
