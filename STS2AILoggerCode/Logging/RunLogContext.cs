using System.IO;
using System.Linq;
using MegaCrit.Sts2.Core.Runs;

namespace STS2AILogger.STS2AILoggerCode.Logging;

public static class RunLogContext
{
    public static string BuildRunKey(RunState runState)
    {
        string seed = SanitizeForFileName(runState.Rng.StringSeed);
        string character = SanitizeForFileName(runState.Players.FirstOrDefault()?.Character.Id.ToString() ?? "UNKNOWN_CHARACTER");
        string gameMode = SanitizeForFileName(runState.GameMode.ToString());
        return $"{seed}-{character}-A{runState.AscensionLevel}-{gameMode}";
    }

    public static string BuildRunPath(string loggerDir, string runId)
    {
        return Path.Combine(loggerDir, "runs", $"{runId}.jsonl");
    }

    public static string BuildRerunId(string runKey, string sessionId)
    {
        return $"{runKey}-rerun-{sessionId}";
    }

    public static bool HasRunEnded(string path)
    {
        return File.Exists(path) && File.ReadLines(path).Any(line => line.Contains("\"type\":\"run_ended\""));
    }

    private static string SanitizeForFileName(string value)
    {
        char[] chars = value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        string sanitized = new(chars);
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }
}
