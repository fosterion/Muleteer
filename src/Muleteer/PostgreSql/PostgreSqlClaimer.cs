using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Muleteer.Internal;

namespace Muleteer.PostgreSql;

internal sealed class PostgreSqlClaimer : IJobClaimer
{
    private const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    private const int CandidatesPerJob = 16;

    private readonly ConcurrentDictionary<IEntityType, ClaimSql> _sql = new();

    public void Validate(DbContext db, IEntityType jobEntity)
    {
        if (db.Database.ProviderName != NpgsqlProvider)
            throw new InvalidOperationException(
                $"{db.GetType().Name} uses {db.Database.ProviderName}, but its Muleteer jobs are set up for PostgreSQL.");

        SqlFor(db, jobEntity);
    }

    public async Task<ClaimResult> ClaimAsync<TJob>(ClaimRequest<TJob> request, CancellationToken ct)
        where TJob : Job
    {
        var db = request.Db;
        var sql = SqlFor(db, db.Model.FindEntityType(typeof(TJob))!);

        List<long>? candidates = null;
        if (request.Filter is { } filter)
        {
            candidates = await filter
                .Where(j => j.Status == JobStatus.Pending && j.NextAttemptAt <= request.Now)
                .OrderBy(j => j.NextAttemptAt)
                .ThenBy(j => j.Id)
                .Select(j => j.Id)
                .Take(request.Count * CandidatesPerJob)
                .ToListAsync(ct);

            if (candidates.Count == 0)
                return new ClaimResult([], HasMore: false);
        }

        var ids = await db.Database
            .SqlQueryRaw<long>(
                candidates is null ? sql.ClaimDue : sql.ClaimCandidates,
                sql.Parameters(db, request, candidates?.ToArray()))
            .ToListAsync(ct);

        var hasMore = ids.Count == request.Count || candidates?.Count == request.Count * CandidatesPerJob;
        return new ClaimResult(ids, hasMore);
    }

    private ClaimSql SqlFor(DbContext db, IEntityType jobEntity) =>
        _sql.GetOrAdd(jobEntity, static (entity, db) => ClaimSql.Build(db, entity), db);

    private sealed class ClaimSql
    {
        public required string ClaimDue { get; init; }

        public required string ClaimCandidates { get; init; }

        public required RelationalTypeMapping Status { get; init; }
        public required RelationalTypeMapping Time { get; init; }
        public required RelationalTypeMapping Token { get; init; }
        public required RelationalTypeMapping Count { get; init; }
        public required RelationalTypeMapping Candidates { get; init; }

        public static ClaimSql Build(DbContext db, IEntityType entity)
        {
            var jobName = entity.ClrType.Name;
            var tableName = entity.GetTableName() ?? throw new InvalidOperationException($"{jobName} must be mapped to a table.");
            var table = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
            var sqlHelper = db.GetService<ISqlGenerationHelper>();
            var typeMappings = db.GetService<IRelationalTypeMappingSource>();

            var status = entity.FindProperty(nameof(Job.Status))!;
            var nextAttemptAt = entity.FindProperty(nameof(Job.NextAttemptAt))!;
            var leaseToken = entity.FindProperty(nameof(Job.LeaseToken))!;

            string Column(IProperty property) => sqlHelper.DelimitIdentifier(
                property.GetColumnName(table) ?? throw new InvalidOperationException($"{jobName}.{property.Name} is not stored in {tableName}."));

            var from = sqlHelper.DelimitIdentifier(tableName, entity.GetSchema());
            var id = Column(entity.FindProperty(nameof(Job.Id))!);
            var next = Column(nextAttemptAt);
            var attempts = Column(entity.FindProperty(nameof(Job.Attempts))!);

            string Claim(string condition) => $"""
                UPDATE {from} SET {Column(leaseToken)} = @muleteer_token, {attempts} = {attempts} + 1, {next} = @muleteer_leased_until
                WHERE {id} = ANY(ARRAY(
                    SELECT {id} FROM {from}
                    WHERE {Column(status)} = @muleteer_pending AND {next} <= @muleteer_now{condition}
                    ORDER BY {next}, {id}
                    LIMIT @muleteer_count
                    FOR UPDATE SKIP LOCKED))
                RETURNING {id} AS "Value"
                """;

            return new ClaimSql
            {
                ClaimDue = Claim(""),
                ClaimCandidates = Claim($" AND {id} = ANY(@muleteer_candidates)"),
                Status = status.GetRelationalTypeMapping(),
                Time = nextAttemptAt.GetRelationalTypeMapping(),
                Token = leaseToken.GetRelationalTypeMapping(),
                Count = typeMappings.FindMapping(typeof(int))!,
                Candidates = typeMappings.FindMapping(typeof(long[]))!,
            };
        }

        public object[] Parameters<TJob>(DbContext db, ClaimRequest<TJob> request, long[]? candidates) where TJob : Job
        {
            using var command = db.Database.GetDbConnection().CreateCommand();
            List<object> parameters =
            [
                Token.CreateParameter(command, "muleteer_token", request.Token, nullable: false),
                Time.CreateParameter(command, "muleteer_leased_until", request.LeasedUntil, nullable: false),
                Time.CreateParameter(command, "muleteer_now", request.Now, nullable: false),
                Status.CreateParameter(command, "muleteer_pending", JobStatus.Pending, nullable: false),
                Count.CreateParameter(command, "muleteer_count", request.Count, nullable: false),
            ];

            if (candidates is not null)
                parameters.Add(Candidates.CreateParameter(command, "muleteer_candidates", candidates, nullable: false));

            return parameters.ToArray();
        }
    }
}
