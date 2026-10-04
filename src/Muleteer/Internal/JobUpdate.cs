namespace Muleteer.Internal;

internal readonly record struct JobUpdate(JobStatus Status, int Attempts, DateTimeOffset NextAttemptAt, string? LastError)
{
    private const int MaxErrorLength = 4000;

    private const string ExhaustedReason =
        "The previous attempt never finished (the process died or the lease expired) and no attempts are left.";

    public static JobUpdate Completed(JobResult result, int attempts, string? lastError, DateTimeOffset now)
    {
        if (result.FailReason is { } reason)
            return new(JobStatus.Failed, attempts, now, reason);

        return new(JobStatus.Succeeded, attempts, now, lastError);
    }

    public static JobUpdate Failed(Exception error, int attempts, JobOptions options, DateTimeOffset now, double jitter)
    {
        var message = error.ToString();

        if (message.Length > MaxErrorLength)
            message = message[..MaxErrorLength];

        if (attempts >= options.MaxAttempts)
            return new(JobStatus.Failed, attempts, now, message);

        var delay = Backoff.Delay(attempts, options.RetryDelay, options.RetryMaxDelay, jitter);
        return new(JobStatus.Pending, attempts, now + delay, message);
    }

    public static JobUpdate Released(int attempts, string? lastError, DateTimeOffset now) =>
        new(JobStatus.Pending, attempts - 1, now, lastError);

    public static JobUpdate Exhausted(int attempts, DateTimeOffset now) =>
        new(JobStatus.Failed, attempts - 1, now, ExhaustedReason);
}
