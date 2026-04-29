using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using STS2AILogger.STS2AILoggerCode.Logging;

namespace STS2AILogger.Patches;

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.OnMapPointSelectedLocally))]
public static class MapNodeSelectedPatch
{
    public static void Prefix(NMapScreen __instance, NMapPoint point)
    {
        try
        {
            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            MapSelectionLogContext.LogSelection(__instance, point, runState);
        }
        catch (Exception ex)
        {
            EventLogger.Error($"Failed to log map node selection: {ex}");
        }
    }
}
