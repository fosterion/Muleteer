using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Muleteer.Internal;

namespace Muleteer;

public static class DI
{
    public static IServiceCollection AddMuleteer<TContext>(this IServiceCollection services, Action<MuleteerBuilder<TContext>> configure)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<JobOptions>, JobOptionsValidator>());

        var builder = new MuleteerBuilder<TContext>(services);
        configure(builder);

        if (builder.Claimer is null)
            throw new InvalidOperationException(
                $"Choose the database of Muleteer jobs in {typeof(TContext).Name}: call UsePostgreSql().");

        return services;
    }
}
