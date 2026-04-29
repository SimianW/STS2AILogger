namespace STS2AILogger.STS2AILoggerCode.Logging;

public sealed record EventEnvelope(
    int SchemaVersion,
    long EventIndex,
    string TimestampUtc,
    string? SessionId,
    string? RunId,
    string? RunKey,
    string DataType,
    string Type,
    string? Summary,
    object? Payload);
