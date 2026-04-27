using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2AILogger.STS2AILoggerCode.Logging;

namespace STS2AILogger.Patches;

[HarmonyPatch(typeof(PotionModel), nameof(PotionModel.OnUseWrapper))]
public static class PotionUseStartedPatch
{
    public static void Prefix(PotionModel __instance, Creature? target)
    {
        try
        {
            Player owner = __instance.Owner;
            RunState? runState = owner.RunState as RunState;
            CombatState? combatState = owner.Creature.CombatState;

            EventLogger.SetRunContext(runState);
            EventLogger.Write("potion_use_started", new
            {
                potion = GameSnapshots.Potion(__instance),
                slot_index = owner.GetPotionSlotIndex(__instance),
                target = GameSnapshots.Creature(target),
                player_before = GameSnapshots.Player(owner),
                combat_before = GameSnapshots.Combat(combatState),
                run = GameSnapshots.Run(runState)
            }, $"potion use started: {__instance.Id}");
        }
        catch (Exception ex)
        {
            EventLogger.Error($"Failed to log potion use start: {ex}");
        }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPotionUsed))]
public static class PotionUsedPatch
{
    public static void Prefix(IRunState runState, CombatState? combatState, PotionModel potion, Creature? target)
    {
        try
        {
            Player owner = potion.Owner;
            EventLogger.SetRunContext(runState as RunState);
            EventLogger.Write("potion_used", new
            {
                potion = GameSnapshots.Potion(potion),
                target = GameSnapshots.Creature(target),
                player = GameSnapshots.Player(owner),
                local_player = GameSnapshots.Player(LocalContext.GetMe(runState)),
                combat = GameSnapshots.Combat(combatState),
                run = GameSnapshots.Run(runState as RunState)
            }, $"potion used: {potion.Id}");
        }
        catch (Exception ex)
        {
            EventLogger.Error($"Failed to log potion used: {ex}");
        }
    }
}

[HarmonyPatch(typeof(PotionCmd), nameof(PotionCmd.Discard))]
public static class PotionDiscardedPatch
{
    public static void Prefix(PotionModel potion)
    {
        try
        {
            Player owner = potion.Owner;
            RunState? runState = owner.RunState as RunState;
            CombatState? combatState = owner.Creature.CombatState;

            EventLogger.SetRunContext(runState);
            EventLogger.Write("potion_discarded", new
            {
                potion = GameSnapshots.Potion(potion),
                slot_index = owner.GetPotionSlotIndex(potion),
                player_before = GameSnapshots.Player(owner),
                combat_before = GameSnapshots.Combat(combatState),
                run = GameSnapshots.Run(runState)
            }, $"potion discarded: {potion.Id}");
        }
        catch (Exception ex)
        {
            EventLogger.Error($"Failed to log potion discarded: {ex}");
        }
    }
}
