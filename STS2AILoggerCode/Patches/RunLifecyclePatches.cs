using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Runs;
using STS2AILogger.STS2AILoggerCode.Logging;

namespace STS2AILogger.Patches;

[HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded))]
public static class RunEndedPatch
{
    public static void Prefix(bool isVictory)
    {
        EventLogger.WriteSafe("run_ended", () =>
        {
            RunState? state = RunManager.Instance.DebugOnlyGetState();
            EventLogger.SetRunContext(state);
            return new
            {
                victory = isVictory,
                abandoned = RunManager.Instance.IsAbandoned,
                run_time = RunManager.Instance.RunTime,
                run = GameSnapshots.Run(state),
                local_player = GameSnapshots.Player(LocalContext.GetMe(state))
            };
        }, isVictory ? "run ended: victory" : "run ended: not victory");
    }
}
