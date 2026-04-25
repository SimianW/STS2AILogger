using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace STS2AILogger.STS2AILoggerCode.Logging;

public static class EventLogger
{
    private static readonly object Lock = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private static string? _logPath;
    private static long _eventIndex;

    public static string? LogPath => _logPath;

    public static void Info(string message)
    {
        MainFile.Logger.Info(message);
    }

    public static void Debug(string message)
    {
        MainFile.Logger.Debug(message);
    }

    public static void Error(string message)
    {
        MainFile.Logger.Error(message);
    }

    public static void Initialize()
    {
        try
        {
            string userDir = ProjectSettings.GlobalizePath("user://");
            string loggerDir = Path.Combine(userDir, "STS2AILogger");
            Directory.CreateDirectory(loggerDir);

            string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            _logPath = Path.Combine(loggerDir, $"events-{timestamp}.jsonl");
            _eventIndex = 0;

            Write("logger_initialized", new
            {
                log_path = _logPath
            });

            Info($"event log: {_logPath}");
        }
        catch (Exception ex)
        {
            Error($"Failed to initialize event logger: {ex}");
        }
    }

    public static void Write(string type, object? payload = null)
    {
        try
        {
            if (_logPath == null)
            {
                Initialize();
            }

            var envelope = new Dictionary<string, object?>
            {
                ["event_index"] = _eventIndex++,
                ["timestamp_utc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["type"] = type,
                ["payload"] = payload
            };

            string line = JsonSerializer.Serialize(envelope, JsonOptions);
            Debug($"logging event #{envelope["event_index"]}: {type}");

            lock (Lock)
            {
                File.AppendAllText(_logPath!, line + System.Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            Error($"Failed to write event '{type}': {ex}");
        }
    }

    public static void WriteSafe(string type, Func<object?> payloadFactory)
    {
        try
        {
            Write(type, payloadFactory());
        }
        catch (Exception ex)
        {
            Error($"Failed to build event '{type}': {ex}");
        }
    }
}
