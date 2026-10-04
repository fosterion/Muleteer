using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Muleteer.Internal;

internal sealed class JobRunner<TContext, TJob, THandler> : BackgroundService
    where TContext : DbContext
    where TJob : Job
    where THandler : class, IJobHandler<TJob>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<JobOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly IJobClaimer _claimer;
    private readonly string _name;
    private readonly CancellationTokenSource _abort = new();

    private JobOptions? _lastValidOptions;

    public JobRunner(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<JobOptions> options,
        TimeProvider time,
        ILoggerFactory loggerFactory,
        IJobClaimer claimer,
        string name)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _time = time;
        _logger = loggerFactory.CreateLogger($"Muleteer.{name}");
        _claimer = claimer;
        _name = name;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (cancellationToken.IsCancellationRequested)
            _abort.Cancel();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var concurrency = CurrentOptions().Concurrency;
        using var slots = new SemaphoreSlim(concurrency, concurrency);

        _logger.RunnerStarted(_name, concurrency);

        try
        {
            await DispatchAsync(slots, concurrency, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        for (var i = 0; i < concurrency; i++)
            await slots.WaitAsync(CancellationToken.None);

        _logger.RunnerStopped(_name);
    }

    private JobOptions CurrentOptions()
    {
        try
        {
            return _lastValidOptions = _options.Get(_name);
        }
        catch (OptionsValidationException ex) when (_lastValidOptions is not null)
        {
            _logger.InvalidOptions(ex, _name);
            return _lastValidOptions;
        }
    }

    private async Task DispatchAsync(SemaphoreSlim slots, int concurrency, CancellationToken ct)
    {
        while (true)
        {
            var options = CurrentOptions();

            if (!options.Enabled)
            {
                await Task.Delay(options.PollInterval, _time, ct);
                continue;
            }

            await slots.WaitAsync(ct);
            var free = 1;
            while (free < concurrency && slots.Wait(0))
                free++;

            var started = 0;
            var hasMore = false;
            try
            {
                var token = Guid.NewGuid();
                var claim = await ClaimAsync(free, token, options, ct);
                foreach (var id in claim.Ids)
                {
                    _ = Task.Run(() => ProcessAsync(id, token, options, slots), CancellationToken.None);
                    started++;
                }

                hasMore = claim.HasMore;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.ClaimFailed(ex, _name);
            }
            finally
            {
                if (free > started)
                    slots.Release(free - started);
            }

            if (!hasMore)
                await Task.Delay(options.PollInterval, _time, ct);
        }
    }

    private async Task<ClaimResult> ClaimAsync(int count, Guid token, JobOptions options, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var handler = scope.ServiceProvider.GetRequiredService<THandler>();

        var jobs = db.Set<TJob>();
        var filtered = handler.Filter(jobs);
        var now = _time.GetUtcNow();

        var request = new ClaimRequest<TJob>(
            db, ReferenceEquals(filtered, jobs) ? null : filtered, count, token, now, now + options.LeaseDuration);

        return await _claimer.ClaimAsync(request, ct);
    }

    private async Task ProcessAsync(long id, Guid token, JobOptions options, SemaphoreSlim slots)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TContext>();
            var handler = scope.ServiceProvider.GetRequiredService<THandler>();

            var job = await JobStore.LoadAsync(db.Set<TJob>(), id, token, _abort.Token);
            if (job is null)
            {
                _logger.LeaseLost(_name, id);
                return;
            }

            var attempts = job.Attempts;
            var lastError = job.LastError;

            if (attempts > options.MaxAttempts)
            {
                var exhausted = JobUpdate.Exhausted(attempts, _time.GetUtcNow());
                if (!await JobStore.SettleAsync<TJob>(db, id, token, exhausted, CancellationToken.None))
                    _logger.LeaseLost(_name, id);
                else
                    _logger.JobFailed(null, _name, id, exhausted.Attempts, exhausted.LastError!);

                return;
            }

            var leaseLeft = job.NextAttemptAt - _time.GetUtcNow();

            var (result, error) = await RunHandlerAsync(handler, job, leaseLeft);

            if (error is null)
            {
                var outcome = JobUpdate.Completed(result, attempts, lastError, _time.GetUtcNow());
                try
                {
                    if (!await JobStore.CommitAsync(db, job, token, outcome, _abort.Token))
                        _logger.LeaseLost(_name, id);
                    else if (result.FailReason is { } reason)
                        _logger.JobFailed(null, _name, id, attempts, reason);
                    else
                        _logger.JobSucceeded(_name, id);

                    return;
                }
                catch (Exception ex) when (!_abort.IsCancellationRequested)
                {
                    error = ex;
                }
            }

            var now = _time.GetUtcNow();
            var released = error is OperationCanceledException && _abort.IsCancellationRequested;
            var failed = released
                ? JobUpdate.Released(attempts, lastError, now)
                : JobUpdate.Failed(error, attempts, options, now, Random.Shared.NextDouble());

            if (!await JobStore.SettleAsync<TJob>(db, id, token, failed, CancellationToken.None))
                _logger.LeaseLost(_name, id);
            else if (released)
                _logger.JobReleased(_name, id);
            else if (failed.Status == JobStatus.Failed)
                _logger.JobFailed(error, _name, id, attempts, error.Message);
            else
                _logger.JobFailed(error, _name, id, attempts, failed.NextAttemptAt);
        }
        catch (Exception ex)
        {
            _logger.ProcessingFailed(ex, _name, id);
        }
        finally
        {
            slots.Release();
        }
    }

    private async Task<(JobResult Result, Exception? Error)> RunHandlerAsync(THandler handler, TJob job, TimeSpan leaseLeft)
    {
        using var leaseTimeout = new CancellationTokenSource(leaseLeft > TimeSpan.Zero ? leaseLeft : TimeSpan.Zero, _time);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_abort.Token, leaseTimeout.Token);

        try
        {
            return (await handler.HandleAsync(job, cancellation.Token), null);
        }
        catch (Exception ex)
        {
            return (default, ex);
        }
    }
}
