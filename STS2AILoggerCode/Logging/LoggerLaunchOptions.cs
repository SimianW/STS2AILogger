using System;
using System.Collections.Generic;
using Godot;

namespace STS2AILogger.STS2AILoggerCode.Logging;

public sealed record LoggerLaunchOptions(string DataType)
{
    private const string DefaultDataType = "manual";
    private const int MaxDataTypeLength = 64;

    private static readonly string[] DataTypeKeys =
    {
        "sts2ailogger-data-type",
        "sts2-ai-logger-data-type",
        "ailogger-data-type",
        "data-type",
        "sts2ailogger-run-mode",
        "sts2-ai-logger-run-mode",
        "ailogger-run-mode",
        "run-mode"
    };

    public static LoggerLaunchOptions FromProcess()
    {
        string? value = FirstNonEmpty(
            TryGetFromArgs(OS.GetCmdlineUserArgs()),
            TryGetFromArgs(OS.GetCmdlineArgs()),
            Environment.GetEnvironmentVariable("STS2AILOGGER_DATA_TYPE"),
            Environment.GetEnvironmentVariable("STS2_AI_LOGGER_DATA_TYPE"),
            Environment.GetEnvironmentVariable("STS2AILOGGER_RUN_MODE"));

        return new LoggerLaunchOptions(NormalizeDataType(value));
    }

    private static string? TryGetFromArgs(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            string option = arg[2..];
            int separatorIndex = option.IndexOf('=', StringComparison.Ordinal);
            string key = separatorIndex >= 0 ? option[..separatorIndex] : option;
            if (!IsDataTypeKey(key))
            {
                continue;
            }

            if (separatorIndex >= 0)
            {
                return option[(separatorIndex + 1)..];
            }

            if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static bool IsDataTypeKey(string key)
    {
        foreach (string dataTypeKey in DataTypeKeys)
        {
            if (string.Equals(key, dataTypeKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string NormalizeDataType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultDataType;
        }

        string trimmed = value.Trim().ToLowerInvariant();
        if (trimmed.Length > MaxDataTypeLength)
        {
            trimmed = trimmed[..MaxDataTypeLength];
        }

        Span<char> normalized = stackalloc char[trimmed.Length];
        int normalizedLength = 0;
        foreach (char ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')
            {
                normalized[normalizedLength++] = ch;
            }
        }

        return normalizedLength == 0 ? DefaultDataType : new string(normalized[..normalizedLength]);
    }
}
