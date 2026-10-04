using Microsoft.Extensions.Options;

namespace Muleteer.Internal;

internal sealed class JobOptionsValidator : IValidateOptions<JobOptions>
{
    private static readonly TimeSpan MaxWait = TimeSpan.FromMilliseconds(int.MaxValue);
    private static readonly TimeSpan RetryMaxDelayLimit = TimeSpan.FromDays(30);

    public ValidateOptionsResult Validate(string? name, JobOptions options)
    {
        List<string> errors = [];

        if (options.Concurrency < 1)
            errors.Add("Concurrency must be at least 1.");
        if (options.PollInterval <= TimeSpan.Zero || options.PollInterval > MaxWait)
            errors.Add($"PollInterval must be positive and at most {MaxWait}.");
        if (options.LeaseDuration <= TimeSpan.Zero || options.LeaseDuration > MaxWait)
            errors.Add($"LeaseDuration must be positive and at most {MaxWait}.");
        if (options.MaxAttempts < 1)
            errors.Add("MaxAttempts must be at least 1.");
        if (options.RetryDelay < TimeSpan.Zero)
            errors.Add("RetryDelay must not be negative.");
        if (options.RetryMaxDelay < options.RetryDelay || options.RetryMaxDelay > RetryMaxDelayLimit)
            errors.Add($"RetryMaxDelay must be between RetryDelay and {RetryMaxDelayLimit.TotalDays} days.");

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors.Select(e => $"Muleteer job '{name}': {e}"));
    }
}
