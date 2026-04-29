using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using STS2AILogger.STS2AILoggerCode.Logging;

namespace STS2AILogger.Patches;

[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseACardScreen))]
public static class ChooseACardSelectionPatch
{
    public static void Prefix(PlayerChoiceContext context, IReadOnlyList<CardModel> cards, Player player, bool canSkip, out CardSelectionLogState __state)
    {
        __state = CardSelectionPatchHelpers.Offer("choose_a_card", player, cards, null, canSkip, false, context.LastInvolvedModel);
    }

    public static void Postfix(ref Task<CardModel?> __result, CardSelectionLogState __state)
    {
        __result = CardSelectionPatchHelpers.LogSelectedCard(__result, __state);
    }
}

[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGridForRewards))]
public static class SimpleGridRewardSelectionPatch
{
    public static void Prefix(PlayerChoiceContext context, List<CardCreationResult> cards, Player player, CardSelectorPrefs prefs, out CardSelectionLogState __state)
    {
        __state = CardSelectionPatchHelpers.Offer("simple_grid_reward", player, cards.Select(card => card.Card), prefs, false, prefs.RequireManualConfirmation, context.LastInvolvedModel);
    }

    public static void Postfix(ref Task<IEnumerable<CardModel>> __result, CardSelectionLogState __state)
    {
        __result = CardSelectionPatchHelpers.LogSelectedCards(__result, __state);
    }
}

[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGrid))]
public static class SimpleGridSelectionPatch
{
    public static void Prefix(PlayerChoiceContext context, IReadOnlyList<CardModel> cardsIn, Player player, CardSelectorPrefs prefs, out CardSelectionLogState __state)
    {
        __state = CardSelectionPatchHelpers.Offer("simple_grid", player, cardsIn, prefs, false, prefs.RequireManualConfirmation, context.LastInvolvedModel);
    }

    public static void Postfix(ref Task<IEnumerable<CardModel>> __result, CardSelectionLogState __state)
    {
        __result = CardSelectionPatchHelpers.LogSelectedCards(__result, __state);
    }
}

[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckForUpgrade))]
public static class DeckUpgradeSelectionPatch
{
    public static void Prefix(Player player, CardSelectorPrefs prefs, out CardSelectionLogState __state)
    {
        IEnumerable<CardModel> cards = PileType.Deck.GetPile(player).Cards.Where(card => card.IsUpgradable);
        __state = CardSelectionPatchHelpers.Offer("deck_upgrade", player, cards, prefs, false, prefs.RequireManualConfirmation, null);
    }

    public static void Postfix(ref Task<IEnumerable<CardModel>> __result, CardSelectionLogState __state)
    {
        __result = CardSelectionPatchHelpers.LogSelectedCards(__result, __state);
    }
}

[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckForTransformation))]
public static class DeckTransformSelectionPatch
{
    public static void Prefix(Player player, CardSelectorPrefs prefs, out CardSelectionLogState __state)
    {
        IEnumerable<CardModel> cards = PileType.Deck.GetPile(player).Cards.Where(card => card.Type != CardType.Quest && card.IsTransformable);
        __state = CardSelectionPatchHelpers.Offer("deck_transform", player, cards, prefs, false, prefs.RequireManualConfirmation, null);
    }

    public static void Postfix(ref Task<IEnumerable<CardModel>> __result, CardSelectionLogState __state)
    {
        __result = CardSelectionPatchHelpers.LogSelectedCards(__result, __state);
    }
}

[HarmonyPatch]
public static class DeckEnchantSelectionPatch
{
    public static System.Reflection.MethodBase TargetMethod()
    {
        return AccessTools.Method(
            typeof(CardSelectCmd),
            nameof(CardSelectCmd.FromDeckForEnchantment),
            new[] { typeof(IReadOnlyList<CardModel>), typeof(EnchantmentModel), typeof(int), typeof(CardSelectorPrefs) });
    }

    public static void Prefix(IReadOnlyList<CardModel> cards, EnchantmentModel enchantment, int amount, CardSelectorPrefs prefs, out CardSelectionLogState __state)
    {
        __state = CardSelectionPatchHelpers.Offer("deck_enchant", cards.FirstOrDefault()?.Owner, cards, prefs, false, prefs.RequireManualConfirmation, enchantment);
    }

    public static void Postfix(ref Task<IEnumerable<CardModel>> __result, CardSelectionLogState __state)
    {
        __result = CardSelectionPatchHelpers.LogSelectedCards(__result, __state);
    }
}

[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckGeneric))]
public static class DeckGenericSelectionPatch
{
    public static void Prefix(Player player, CardSelectorPrefs prefs, Func<CardModel, bool>? filter, Func<CardModel, int>? sortingOrder, out CardSelectionLogState __state)
    {
        List<CardModel> cards = PileType.Deck.GetPile(player).Cards.ToList();
        cards = filter == null ? cards : cards.Where(filter).ToList();
        if (sortingOrder != null)
        {
            cards = cards.OrderBy(sortingOrder).ToList();
        }

        __state = CardSelectionPatchHelpers.Offer("deck_select", player, cards, prefs, false, prefs.RequireManualConfirmation, null);
    }

    public static void Postfix(ref Task<IEnumerable<CardModel>> __result, CardSelectionLogState __state)
    {
        __result = CardSelectionPatchHelpers.LogSelectedCards(__result, __state);
    }
}

[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHand))]
public static class HandSelectionPatch
{
    public static void Prefix(PlayerChoiceContext context, Player player, CardSelectorPrefs prefs, Func<CardModel, bool>? filter, AbstractModel source, out CardSelectionLogState? __state)
    {
        if (CardSelectionPatchHelpers.SuppressHandSelectionDepth > 0)
        {
            __state = null;
            return;
        }

        IEnumerable<CardModel> cards = PileType.Hand.GetPile(player).Cards.Where(filter ?? (_ => true));
        __state = CardSelectionPatchHelpers.Offer("hand_select", player, cards, prefs, false, prefs.RequireManualConfirmation, source ?? context.LastInvolvedModel);
    }

    public static void Postfix(ref Task<IEnumerable<CardModel>> __result, CardSelectionLogState? __state)
    {
        __result = CardSelectionPatchHelpers.LogSelectedCards(__result, __state);
    }
}

[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHandForDiscard))]
public static class HandDiscardSelectionPatch
{
    public static void Prefix(PlayerChoiceContext context, Player player, CardSelectorPrefs prefs, Func<CardModel, bool>? filter, AbstractModel source, out CardSelectionLogState __state)
    {
        CardSelectionPatchHelpers.SuppressHandSelectionDepth++;
        IEnumerable<CardModel> cards = PileType.Hand.GetPile(player).Cards.Where(filter ?? (_ => true));
        __state = CardSelectionPatchHelpers.Offer("hand_discard", player, cards, prefs, false, prefs.RequireManualConfirmation, source ?? context.LastInvolvedModel);
    }

    public static void Postfix(ref Task<IEnumerable<CardModel>> __result, CardSelectionLogState __state)
    {
        __result = CardSelectionPatchHelpers.LogSelectedCards(__result, __state);
        CardSelectionPatchHelpers.SuppressHandSelectionDepth--;
    }
}

[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHandForUpgrade))]
public static class HandUpgradeSelectionPatch
{
    public static void Prefix(PlayerChoiceContext context, Player player, AbstractModel source, out CardSelectionLogState __state)
    {
        IEnumerable<CardModel> cards = PileType.Hand.GetPile(player).Cards.Where(card => card.IsUpgradable);
        __state = CardSelectionPatchHelpers.Offer("hand_upgrade", player, cards, null, false, false, source ?? context.LastInvolvedModel);
    }

    public static void Postfix(ref Task<CardModel?> __result, CardSelectionLogState __state)
    {
        __result = CardSelectionPatchHelpers.LogSelectedCard(__result, __state);
    }
}

internal static class CardSelectionPatchHelpers
{
    [ThreadStatic]
    public static int SuppressHandSelectionDepth;

    public static CardSelectionLogState Offer(
        string selectionKind,
        Player? player,
        IEnumerable<CardModel>? cards,
        CardSelectorPrefs? prefs,
        bool canSkip,
        bool requiresConfirmation,
        AbstractModel? source)
    {
        CardSelectionLogState state = SelectionSnapshots.Create(selectionKind, player, cards, prefs, canSkip, requiresConfirmation, source);
        EventWriter.WriteCardSelectionOffered(state);
        return state;
    }

    public static async Task<CardModel?> LogSelectedCard(Task<CardModel?> original, CardSelectionLogState? state)
    {
        CardModel? selected = await original;
        if (state != null)
        {
            EventWriter.WriteCardSelectionSelected(state, selected == null ? Array.Empty<CardModel>() : new[] { selected });
        }

        return selected;
    }

    public static async Task<IEnumerable<CardModel>> LogSelectedCards(Task<IEnumerable<CardModel>> original, CardSelectionLogState? state)
    {
        IEnumerable<CardModel> selected = await original;
        List<CardModel> selectedList = selected.ToList();
        if (state != null)
        {
            EventWriter.WriteCardSelectionSelected(state, selectedList);
        }

        return selectedList;
    }
}
