namespace Muleteer.Internal;

internal static class Backoff
{
    private const int MaxExponent = 62;

    public static TimeSpan Delay(int attempt, TimeSpan baseDelay, TimeSpan maxDelay, double jitter)
    {
        var exponential = baseDelay.Ticks * Math.Pow(2, Math.Clamp(attempt - 1, 0, MaxExponent));
        var capped = Math.Min(exponential, maxDelay.Ticks);

        return TimeSpan.FromTicks((long)(capped * (1 - 0.2 * jitter)));
    }
}
