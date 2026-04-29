using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using STS2AILogger.STS2AILoggerCode.Logging;

namespace STS2AILogger.STS2AILoggerCode;

//You're recommended but not required to keep all your code in this package and all your assets in the STS2AILogger folder.
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "STS2AILogger"; //At the moment, this is used only for the Logger and harmony names.

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        EventLogger.Info(@" /\_/\");
        EventLogger.Info($"( o.o )  STS2 AI Logger {EventLogger.Version}");
        EventLogger.Info(@" > ^ <   JSONL facts, one fact at a time");
        EventLogger.Info($"STS2AILogger {EventLogger.Version} initializing");
        EventLogger.Initialize();

        Harmony harmony = new(ModId);
        harmony.PatchAll();
        RuntimeEventHooks.Initialize();

        EventLogger.Info($"STS2AILogger {EventLogger.Version} ready");
    }
}
