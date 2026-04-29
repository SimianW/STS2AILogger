using System.Runtime.CompilerServices;
using System.Threading;
using MegaCrit.Sts2.Core.Combat;

namespace STS2AILogger.STS2AILoggerCode.Logging;

public static class CardPlayLogContext
{
    private sealed class CardPlayLogIdentity
    {
        public CardPlayLogIdentity(string correlationId)
        {
            CorrelationId = correlationId;
        }

        public string CorrelationId { get; }
    }

    private static long _nextCorrelationId;
    private static readonly ConditionalWeakTable<CardPlay, CardPlayLogIdentity> CorrelationIds = new();

    public static string GetOrCreateCorrelationId(CardPlay cardPlay)
    {
        return CorrelationIds.GetValue(cardPlay, static _ =>
        {
            string correlationId = $"card-play-{Interlocked.Increment(ref _nextCorrelationId)}";
            return new CardPlayLogIdentity(correlationId);
        }).CorrelationId;
    }

    public static void Release(CardPlay cardPlay)
    {
        CorrelationIds.Remove(cardPlay);
    }
}
