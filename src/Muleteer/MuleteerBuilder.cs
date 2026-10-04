using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Muleteer.Internal;

namespace Muleteer;

public class MuleteerBuilder<TContext>
    where TContext : DbContext
{
    private readonly HashSet<string> _jobs = [];

    internal IJobClaimer? Claimer { get; private set; }
    public IServiceCollection Services { get; }

    internal MuleteerBuilder(IServiceCollection services)
    {
        Services = services;
    }

    internal MuleteerBuilder<TContext> UseClaimer(IJobClaimer claimer)
    {
        ArgumentNullException.ThrowIfNull(claimer);

        if (Claimer is not null)
            throw new InvalidOperationException($"The database of Muleteer jobs in {typeof(TContext).Name} is already chosen.");

        Claimer = claimer;
        return this;
    }

    public MuleteerBuilder<TContext> AddJobHandler<TJob, THandler>(Action<JobOptions>? configure = null)
        where TJob : Job
        where THandler : class, IJobHandler<TJob>
    {
        var name = typeof(TJob).Name;

        if (!_jobs.Add(name))
            throw new InvalidOperationException($"{name} already has a handler.");

        var options = Services.AddOptions<JobOptions>(name);

        if (configure is not null)
            options.Configure(configure);

        options.BindConfiguration($"Muleteer:Jobs:{name}");
        options.ValidateOnStart();

        Services.TryAddScoped<THandler>();
        Services.AddSingleton<IHostedService>(sp => CreateRunner<TJob, THandler>(sp, name));

        return this;
    }

    private IHostedService CreateRunner<TJob, THandler>(IServiceProvider services, string name)
        where TJob : Job
        where THandler : class, IJobHandler<TJob>
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var jobName = typeof(TJob).Name;

        var entity = db.Model.FindEntityType(typeof(TJob))
            ?? throw new InvalidOperationException($"{jobName} is not an entity of {typeof(TContext).Name}.");

        if (entity.FindPrimaryKey()?.Properties is not [{ Name: nameof(Job.Id) }])
            throw new InvalidOperationException($"{jobName} must have Id as its primary key.");

        if (entity.FindDiscriminatorProperty() is not null)
            throw new InvalidOperationException($"{jobName} shares its table with other entity types; Muleteer needs a table per job type.");

        Claimer!.Validate(db, entity);

        return new JobRunner<TContext, TJob, THandler>(
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<IOptionsMonitor<JobOptions>>(),
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<ILoggerFactory>(),
            Claimer,
            name);
    }
}
