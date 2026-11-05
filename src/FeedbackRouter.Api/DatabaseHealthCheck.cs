using FeedbackRouter.Infra.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FeedbackRouter.Api;

public sealed class DatabaseHealthCheck(FeedbackDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await db.Database.CanConnectAsync(ct)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Cannot connect to the database.");
}
