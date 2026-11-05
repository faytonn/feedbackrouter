using FeedbackRouter.Infra.Data.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FeedbackRouter.Infra.Data;

public static class DatabaseSetup
{
    /// <summary>
    /// Registers <see cref="FeedbackDbContext"/> on the provider named by
    /// Database:Provider: SqlServer (default, local and Azure) or Postgres (Railway).
    /// </summary>
    public static IServiceCollection AddFeedbackDatabase(this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("FeedbackRouter");
        var provider = config["Database:Provider"] ?? "SqlServer";

        if (string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            services.AddDbContext<FeedbackDbContext, PostgresFeedbackDbContext>(o =>
                o.UseNpgsql(connection, npgsql => npgsql.EnableRetryOnFailure()));
        }
        else if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            services.AddDbContext<FeedbackDbContext>(o =>
                o.UseSqlServer(connection, sql => sql.EnableRetryOnFailure()));
        }
        else
        {
            throw new InvalidOperationException($"Database:Provider must be SqlServer or Postgres, not '{provider}'.");
        }

        return services;
    }
}
