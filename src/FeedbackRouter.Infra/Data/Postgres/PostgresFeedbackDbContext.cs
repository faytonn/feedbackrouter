using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FeedbackRouter.Infra.Data.Postgres;

/// <summary>
/// The same model on PostgreSQL, used by the Railway demo. A separate type so
/// its migrations live apart from the SQL Server ones.
/// </summary>
public sealed class PostgresFeedbackDbContext(DbContextOptions<PostgresFeedbackDbContext> options)
    : FeedbackDbContext(options);

/// <summary>Used only by dotnet ef when generating migrations. No database is contacted.</summary>
public sealed class PostgresDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PostgresFeedbackDbContext>
{
    public PostgresFeedbackDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresFeedbackDbContext>()
            .UseNpgsql("Host=localhost;Database=feedbackrouter")
            .Options);
}
