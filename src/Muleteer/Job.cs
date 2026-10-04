using Microsoft.EntityFrameworkCore;

namespace Muleteer;

[Index(nameof(Status), nameof(NextAttemptAt), nameof(Id))]
public abstract class Job
{
    public long Id { get; init; }
    public int Attempts { get; internal set; }
    public DateTimeOffset NextAttemptAt { get; init => field = value.ToUniversalTime(); }
    public Guid? LeaseToken { get; internal set; }
    public string? LastError { get; internal set; }
    public JobStatus Status { get; internal set; }
}
