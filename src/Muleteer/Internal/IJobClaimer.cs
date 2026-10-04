using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Muleteer.Internal;

internal interface IJobClaimer
{
    void Validate(DbContext db, IEntityType jobEntity);
    Task<ClaimResult> ClaimAsync<TJob>(ClaimRequest<TJob> request, CancellationToken ct)
        where TJob : Job;
}

internal sealed record ClaimRequest<TJob>(
    DbContext Db,
    IQueryable<TJob>? Filter,
    int Count,
    Guid Token,
    DateTimeOffset Now,
    DateTimeOffset LeasedUntil)
    where TJob : Job;

internal sealed record ClaimResult(
    IReadOnlyList<long> Ids,
    bool HasMore);
