using System;
using STS2AILogger.STS2AILoggerCode.Logging;

namespace STS2AILogger.Patches;

public static class CardModelDescriptionPatch
{
  private static bool _loggedOnce;

  // This was a temporary smoke-test patch. It is intentionally not annotated
  // with [HarmonyPatch], so normal AI logging runs do not install it.
  public static void Postfix(object __instance)
  {
    try
    {
      if (_loggedOnce)
        return;

      _loggedOnce = true;

      EventLogger.Info("CardModel.Description patch was called successfully.");
      EventLogger.Info($"Instance type: {__instance.GetType().FullName}");
    }
    catch (Exception ex)
    {
      EventLogger.Error($"Error in CardModelDescriptionPatch: {ex}");
    }
  }
}
