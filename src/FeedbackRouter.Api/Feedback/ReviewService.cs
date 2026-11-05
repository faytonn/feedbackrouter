using FeedbackRouter.Core.Domain;
using FeedbackRouter.Infra.Data;
using Microsoft.EntityFrameworkCore;

namespace FeedbackRouter.Api.Feedback;

public abstract record ReviewOutcome
{
    public sealed record Done : ReviewOutcome;

    public sealed record NotFound : ReviewOutcome;

    public sealed record Conflict(string Message) : ReviewOutcome;
}

/// <summary>
/// "Start review" claims an item for one reviewer by moving it to InReview.
/// The claim ends when the reviewer releases it, decides it, or goes idle;
/// the item then returns to Proposed if it has a proposal, otherwise New.
/// </summary>
public sealed class ReviewService(FeedbackDbContext db, TimeProvider clock)
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>Claims the item, or refreshes the caller's own claim.</summary>
    public async Task<ReviewOutcome> StartAsync(int id, string reviewer, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var idleBefore = now - IdleTimeout;

        var rows = await db.FeedbackItems
            .Where(i => i.Id == id
                        && (i.Status == FeedbackStatus.New
                            || i.Status == FeedbackStatus.Proposed
                            || (i.Status == FeedbackStatus.InReview
                                && (i.ReviewerName == reviewer
                                    || i.ReviewActivityAt == null
                                    || i.ReviewActivityAt < idleBefore))))
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, FeedbackStatus.InReview)
                .SetProperty(i => i.ReviewerName, reviewer)
                .SetProperty(i => i.ReviewActivityAt, now), ct);

        return rows == 1 ? new ReviewOutcome.Done() : await ExplainAsync(id, ct);
    }

    /// <summary>Ends the caller's claim. Releasing an item nobody holds is a no-op.</summary>
    public async Task<ReviewOutcome> ReleaseAsync(int id, string reviewer, CancellationToken ct)
    {
        var rows = await ReturnToQueue(db.FeedbackItems
            .Where(i => i.Id == id && i.Status == FeedbackStatus.InReview && i.ReviewerName == reviewer), ct);
        if (rows == 1)
        {
            return new ReviewOutcome.Done();
        }

        var current = await db.FeedbackItems.AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new { i.Status, i.ReviewerName })
            .SingleOrDefaultAsync(ct);
        return current switch
        {
            null => new ReviewOutcome.NotFound(),
            { Status: FeedbackStatus.InReview } => new ReviewOutcome.Conflict($"Being reviewed by {current.ReviewerName}."),
            _ => new ReviewOutcome.Done(),
        };
    }

    /// <summary>Returns every claim idle for longer than <see cref="IdleTimeout"/> to the queue.</summary>
    public Task<int> ReleaseIdleAsync(CancellationToken ct)
    {
        var idleBefore = clock.GetUtcNow().UtcDateTime - IdleTimeout;
        return ReturnToQueue(db.FeedbackItems
            .Where(i => i.Status == FeedbackStatus.InReview
                        && (i.ReviewActivityAt == null || i.ReviewActivityAt < idleBefore)), ct);
    }

    private static Task<int> ReturnToQueue(IQueryable<FeedbackItem> items, CancellationToken ct) =>
        items.ExecuteUpdateAsync(s => s
            .SetProperty(i => i.Status, i => i.Proposals.Any() ? FeedbackStatus.Proposed : FeedbackStatus.New)
            .SetProperty(i => i.ReviewerName, (string?)null)
            .SetProperty(i => i.ReviewActivityAt, (DateTime?)null), ct);

    private async Task<ReviewOutcome> ExplainAsync(int id, CancellationToken ct)
    {
        var current = await db.FeedbackItems.AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new { i.Status, i.ReviewerName })
            .SingleOrDefaultAsync(ct);
        return current?.Status switch
        {
            null => new ReviewOutcome.NotFound(),
            FeedbackStatus.InReview => new ReviewOutcome.Conflict($"Being reviewed by {current.ReviewerName}."),
            _ => new ReviewOutcome.Conflict($"Item is {current.Status} and cannot be reviewed."),
        };
    }
}

/// <summary>Releases idle review claims once a minute.</summary>
public sealed partial class ReviewSweeper(IServiceScopeFactory scopes, ILogger<ReviewSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var released = await scope.ServiceProvider.GetRequiredService<ReviewService>().ReleaseIdleAsync(stoppingToken);
                if (released > 0)
                {
                    LogReleased(logger, released);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Released {Count} idle review claims")]
    private static partial void LogReleased(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Releasing idle review claims failed")]
    private static partial void LogFailed(ILogger logger, Exception ex);
}
