namespace Muleteer;

public sealed class JobOptions
{
    public bool Enabled { get; set; } = true;
    public int Concurrency { get; set; } = 1;
    public int MaxAttempts { get; set; } = 10;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromHours(1);
}
