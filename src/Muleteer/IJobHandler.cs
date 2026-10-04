namespace Muleteer;

public interface IJobHandler<TJob>
    where TJob : Job
{
    IQueryable<TJob> Filter(IQueryable<TJob> jobs) => jobs;
    IQueryable<TJob> Load(IQueryable<TJob> jobs) => jobs;
    Task<JobResult> HandleAsync(TJob job, CancellationToken cancellationToken);
}

public readonly record struct JobResult
{
    public string? FailReason { get; private init; }

    public static JobResult Succeeded => default;

    public static JobResult Failed(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new() { FailReason = reason };
    }
}
