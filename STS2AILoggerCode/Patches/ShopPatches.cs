using System;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2AILogger.STS2AILoggerCode.Logging;

namespace STS2AILogger.Patches;

[HarmonyPatch(typeof(MerchantInventory), nameof(MerchantInventory.CreateForNormalMerchant))]
public static class ShopItemsOfferedPatch
{
    public static void Postfix(Player player, MerchantInventory __result)
    {
        try
        {
            RunState? runState = player.RunState as RunState;
            EventLogger.SetRunContext(runState);
            EventLogger.Write("shop_items_offered", new
            {
                shop = SnapshotInventory(__result),
                player = GameSnapshots.Player(player),
                run = GameSnapshots.Run(runState)
            }, "shop items offered");
        }
        catch (Exception ex)
        {
            EventLogger.Error($"Failed to log shop items: {ex}");
        }
    }

    internal static object SnapshotInventory(MerchantInventory inventory)
    {
        return new
        {
            cards = inventory.CardEntries.Select(SnapshotEntry).ToList(),
            relics = inventory.RelicEntries.Select(SnapshotEntry).ToList(),
            potions = inventory.PotionEntries.Select(SnapshotEntry).ToList(),
            card_removal = inventory.CardRemovalEntry == null ? null : SnapshotEntry(inventory.CardRemovalEntry)
        };
    }

    internal static object SnapshotEntry(MerchantEntry entry)
    {
        return entry switch
        {
            MerchantCardEntry cardEntry => new
            {
                kind = "card",
                cost = entry.Cost,
                enough_gold = entry.EnoughGold,
                is_stocked = entry.IsStocked,
                is_on_sale = cardEntry.IsOnSale,
                card = GameSnapshots.Card(cardEntry.CreationResult?.Card)
            },
            MerchantRelicEntry relicEntry => new
            {
                kind = "relic",
                cost = entry.Cost,
                enough_gold = entry.EnoughGold,
                is_stocked = entry.IsStocked,
                relic = GameSnapshots.Model(relicEntry.Model)
            },
            MerchantPotionEntry potionEntry => new
            {
                kind = "potion",
                cost = entry.Cost,
                enough_gold = entry.EnoughGold,
                is_stocked = entry.IsStocked,
                potion = GameSnapshots.Potion(potionEntry.Model)
            },
            MerchantCardRemovalEntry removalEntry => new
            {
                kind = "card_removal",
                cost = entry.Cost,
                enough_gold = entry.EnoughGold,
                is_stocked = entry.IsStocked,
                used = removalEntry.Used
            },
            _ => new
            {
                kind = entry.GetType().Name,
                cost = entry.Cost,
                enough_gold = entry.EnoughGold,
                is_stocked = entry.IsStocked
            }
        };
    }

    internal static Player? GetPlayer(MerchantEntry entry)
    {
        return AccessTools.Field(typeof(MerchantEntry), "_player")?.GetValue(entry) as Player;
    }
}

[HarmonyPatch(typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper))]
public static class ShopItemPurchasedPatch
{
    public static void Prefix(MerchantEntry __instance, bool ignoreCost, out ShopPurchaseLogState __state)
    {
        Player? player = ShopItemsOfferedPatch.GetPlayer(__instance);
        __state = new ShopPurchaseLogState(
            player,
            player?.Gold,
            ignoreCost,
            ShopItemsOfferedPatch.SnapshotEntry(__instance));
    }

    public static void Postfix(MerchantEntry __instance, ref Task<bool> __result, ShopPurchaseLogState __state)
    {
        __result = LogAfterPurchase(__result, __instance, __state);
    }

    private static async Task<bool> LogAfterPurchase(Task<bool> original, MerchantEntry entry, ShopPurchaseLogState state)
    {
        bool success = await original;
        if (success)
        {
            LogPurchase(entry, state, null);
        }

        return success;
    }

    internal static void LogPurchase(MerchantEntry entry, ShopPurchaseLogState state, int? goldSpent)
    {
        try
        {
            Player? player = state.Player ?? ShopItemsOfferedPatch.GetPlayer(entry);
            RunState? runState = player?.RunState as RunState;

            EventLogger.SetRunContext(runState);
            EventLogger.Write("shop_item_purchased", new
            {
                item = state.EntryBeforePurchase,
                item_after_purchase = ShopItemsOfferedPatch.SnapshotEntry(entry),
                gold_before = state.GoldBefore,
                gold_after = player?.Gold,
                gold_spent = goldSpent ?? ((state.GoldBefore.HasValue && player != null) ? state.GoldBefore.Value - player.Gold : null),
                ignore_cost = state.IgnoreCost,
                player = GameSnapshots.Player(player),
                local_player = GameSnapshots.Player(runState == null ? null : LocalContext.GetMe(runState)),
                combat = GameSnapshots.Combat(player?.Creature.CombatState),
                run = GameSnapshots.Run(runState)
            }, "shop item purchased");
        }
        catch (Exception ex)
        {
            EventLogger.Error($"Failed to log shop purchase: {ex}");
        }
    }
}

[HarmonyPatch(typeof(MerchantCardRemovalEntry), nameof(MerchantCardRemovalEntry.OnTryPurchaseWrapper))]
public static class ShopCardRemovalPurchasedPatch
{
    public static void Prefix(MerchantCardRemovalEntry __instance, bool ignoreCost, out ShopPurchaseLogState __state)
    {
        Player? player = ShopItemsOfferedPatch.GetPlayer(__instance);
        __state = new ShopPurchaseLogState(
            player,
            player?.Gold,
            ignoreCost,
            ShopItemsOfferedPatch.SnapshotEntry(__instance));
    }

    public static void Postfix(MerchantCardRemovalEntry __instance, ref Task<bool> __result, ShopPurchaseLogState __state)
    {
        __result = LogAfterPurchase(__result, __instance, __state);
    }

    private static async Task<bool> LogAfterPurchase(Task<bool> original, MerchantCardRemovalEntry entry, ShopPurchaseLogState state)
    {
        bool success = await original;
        if (success)
        {
            ShopItemPurchasedPatch.LogPurchase(entry, state, null);
        }

        return success;
    }
}

public sealed record ShopPurchaseLogState(
    Player? Player,
    int? GoldBefore,
    bool IgnoreCost,
    object EntryBeforePurchase);
