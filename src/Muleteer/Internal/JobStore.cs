using Microsoft.EntityFrameworkCore;

namespace Muleteer.Internal;

internal static class JobStore
{
    public static Task<TJob?> LoadAsync<TJob>(IQueryable<TJob> source, long id, Guid token, CancellationToken ct)
        where TJob : Job =>
        source
            .Where(j => j.Id == id && j.LeaseToken == token)
            .SingleOrDefaultAsync(ct);

    public static async Task<bool> CommitAsync<TJob>(DbContext db, TJob job, Guid token, JobUpdate update, CancellationToken ct)
        where TJob : Job
    {
        var entry = db.Entry(job);
        entry.Property(j => j.Status).IsModified = false;
        entry.Property(j => j.Attempts).IsModified = false;
        entry.Property(j => j.NextAttemptAt).IsModified = false;
        entry.Property(j => j.LeaseToken).IsModified = false;
        entry.Property(j => j.LastError).IsModified = false;

        var strategy = db.Database.CreateExecutionStrategy();

        var committed = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            if (!await SettleAsync<TJob>(db, job.Id, token, update, ct))
                return false;

            await db.SaveChangesAsync(acceptAllChangesOnSuccess: false, ct);
            await transaction.CommitAsync(ct);
            return true;
        });

        if (committed)
            db.ChangeTracker.AcceptAllChanges();

        return committed;
    }

    public static async Task<bool> SettleAsync<TJob>(DbContext db, long id, Guid token, JobUpdate update, CancellationToken ct)
        where TJob : Job
    {
        var updated = await db.Set<TJob>()
            .Where(j => j.Id == id && j.LeaseToken == token)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, update.Status)
                .SetProperty(j => j.Attempts, update.Attempts)
                .SetProperty(j => j.NextAttemptAt, update.NextAttemptAt)
                .SetProperty(j => j.LastError, update.LastError)
                .SetProperty(j => j.LeaseToken, (Guid?)null), ct);

        return updated == 1;
    }
}
