using Microsoft.Extensions.Logging;

namespace Muleteer.Internal;

internal static partial class Log
{
    [LoggerMessage(LogLevel.Information, "Muleteer runner for {JobType} started, concurrency {Concurrency}")]
    public static partial void RunnerStarted(this ILogger logger, string jobType, int concurrency);

    [LoggerMessage(LogLevel.Information, "Muleteer runner for {JobType} stopped")]
    public static partial void RunnerStopped(this ILogger logger, string jobType);

    [LoggerMessage(LogLevel.Error, "Invalid options for {JobType} ignored, keeping the previous ones")]
    public static partial void InvalidOptions(this ILogger logger, Exception error, string jobType);

    [LoggerMessage(LogLevel.Error, "Failed to claim {JobType} jobs, retrying after the poll interval")]
    public static partial void ClaimFailed(this ILogger logger, Exception error, string jobType);

    [LoggerMessage(LogLevel.Debug, "{JobType} {JobId} succeeded")]
    public static partial void JobSucceeded(this ILogger logger, string jobType, long jobId);

    [LoggerMessage(LogLevel.Warning, "{JobType} {JobId} failed on attempt {Attempt}, next attempt at {NextAttemptAt:O}")]
    public static partial void JobFailed(this ILogger logger, Exception error, string jobType, long jobId, int attempt, DateTimeOffset nextAttemptAt);

    [LoggerMessage(LogLevel.Error, "{JobType} {JobId} failed after {Attempts} attempts: {Reason}")]
    public static partial void JobFailed(this ILogger logger, Exception? error, string jobType, long jobId, int attempts, string reason);

    [LoggerMessage(LogLevel.Information, "{JobType} {JobId} released on shutdown")]
    public static partial void JobReleased(this ILogger logger, string jobType, long jobId);

    [LoggerMessage(LogLevel.Warning, "{JobType} {JobId} lost its lease to another process, the result is discarded")]
    public static partial void LeaseLost(this ILogger logger, string jobType, long jobId);

    [LoggerMessage(LogLevel.Error, "Processing of {JobType} {JobId} failed, it will run again once the lease expires")]
    public static partial void ProcessingFailed(this ILogger logger, Exception error, string jobType, long jobId);
}
