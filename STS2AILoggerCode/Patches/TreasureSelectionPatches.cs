using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Runs;
using STS2AILogger.STS2AILoggerCode.Logging;

namespace STS2AILogger.Patches;

[HarmonyPatch(typeof(NTreasureRoomRelicCollection), nameof(NTreasureRoomRelicCollection.InitializeRelics))]
public static class TreasureRelicsOfferedPatch
{
    public static void Postfix()
    {
        try
        {
            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            TreasureSelectionLogContext.LogOffered(RunManager.Instance.TreasureRoomRelicSynchronizer, runState);
        }
        catch (Exception ex)
        {
            EventLogger.Error($"Failed to log treasure relics offered: {ex}");
        }
    }
}

[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.PickRelicLocally))]
public static class TreasureRelicPickedPatch
{
    public static void Prefix(TreasureRoomRelicSynchronizer __instance, int? index)
    {
        try
        {
            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            TreasureSelectionLogContext.LogPicked(__instance, index, runState);
        }
        catch (Exception ex)
        {
            EventLogger.Error($"Failed to log treasure relic picked/skipped: {ex}");
        }
    }
}

[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.CompleteWithNoRelics))]
public static class TreasureRelicNoCandidatesPatch
{
    public static void Prefix(TreasureRoomRelicSynchronizer __instance)
    {
        try
        {
            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            TreasureSelectionLogContext.LogNoRelics(__instance, runState);
        }
        catch (Exception ex)
        {
            EventLogger.Error($"Failed to log empty treasure relic selection: {ex}");
        }
    }
}
