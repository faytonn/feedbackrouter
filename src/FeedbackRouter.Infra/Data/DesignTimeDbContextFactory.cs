using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FeedbackRouter.Infra.Data;

/// <summary>Used only by dotnet ef when generating migrations. No database is contacted.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FeedbackDbContext>
{
    public FeedbackDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FeedbackDbContext>()
            .UseSqlServer("Server=localhost;Database=FeedbackRouter;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new FeedbackDbContext(options);
    }
}
