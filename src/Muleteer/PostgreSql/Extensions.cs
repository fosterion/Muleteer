using Microsoft.EntityFrameworkCore;

namespace Muleteer.PostgreSql;

public static class Extensions
{
    public static MuleteerBuilder<TContext> UsePostgreSql<TContext>(this MuleteerBuilder<TContext> builder)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.UseClaimer(new PostgreSqlClaimer());
    }
}
