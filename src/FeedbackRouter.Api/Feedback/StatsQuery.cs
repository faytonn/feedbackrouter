using FeedbackRouter.Core.Domain;
using FeedbackRouter.Infra.Data;
using Microsoft.EntityFrameworkCore;

namespace FeedbackRouter.Api.Feedback;

public sealed class StatsQuery(FeedbackDbContext db, TimeProvider clock)
{
    public const int Days = 30;

    private static readonly (string Key, TimeSpan UpTo)[] Buckets =
    [
        ("< 1h", TimeSpan.FromHours(1)),
        ("1-4h", TimeSpan.FromHours(4)),
        ("4-24h", TimeSpan.FromHours(24)),
        ("1-3d", TimeSpan.FromDays(3)),
        ("> 3d", TimeSpan.MaxValue),
    ];

    private static readonly FeedbackStatus[] OpenStatuses =
        [FeedbackStatus.New, FeedbackStatus.Proposed, FeedbackStatus.InReview, FeedbackStatus.Approved];

    // The data set is small enough to aggregate in memory; this keeps the
    // logic identical on SQL Server and SQLite.
    public async Task<StatsDto> GetAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        var items = await db.FeedbackItems.AsNoTracking()
            .Select(i => new
            {
                i.Status,
                i.SubmittedAt,
                DecidedAt = (DateTime?)i.Decision!.DecidedAt,
                DecisionArea = i.Decision!.FinalArea,
                ProposalArea = i.Proposals.OrderByDescending(p => p.ProposedAt).Select(p => (Area?)p.Area).FirstOrDefault(),
                FiledAt = (DateTime?)i.WorkItemLink!.FiledAt,
            })
            .ToListAsync(ct);

        var byArea = Enum.GetValues<Area>()
            .Select(a => new CountDto(a.ToString(), items.Count(i => (i.DecisionArea ?? i.ProposalArea) == a)))
            .ToList();

        var byStatus = Enum.GetValues<FeedbackStatus>()
            .Select(s => new CountDto(s.ToString(), items.Count(i => i.Status == s)))
            .ToList();

        var perDay = Enumerable.Range(0, Days)
            .Select(offset => today.AddDays(offset - Days + 1))
            .Select(day => new DayCountDto(day, items.Count(i => DateOnly.FromDateTime(i.SubmittedAt) == day)))
            .ToList();

        var triageTimes = items
            .Where(i => i.DecidedAt is not null)
            .Select(i => i.DecidedAt!.Value - i.SubmittedAt)
            .OrderBy(t => t)
            .ToList();

        var buckets = Buckets
            .Select((b, index) => new CountDto(b.Key, triageTimes.Count(t =>
                t < b.UpTo && (index == 0 || t >= Buckets[index - 1].UpTo))))
            .ToList();

        var weekStart = StartOfWeek(now);
        var decided = triageTimes.Count;
        var merged = items.Count(i => i.Status == FeedbackStatus.Merged);

        return new StatsDto(
            byArea,
            byStatus,
            perDay,
            buckets,
            Open: items.Count(i => OpenStatuses.Contains(i.Status)),
            FiledThisWeek: items.Count(i => i.FiledAt >= weekStart),
            MedianTimeToTriageSeconds: decided == 0 ? null : Median(triageTimes).TotalSeconds,
            DuplicateRate: decided == 0 ? null : (double)merged / decided,
            Decided: decided);
    }

    /// <summary>Monday 00:00 UTC of the current week.</summary>
    public static DateTime StartOfWeek(DateTime utcNow)
    {
        var daysSinceMonday = ((int)utcNow.DayOfWeek + 6) % 7;
        return utcNow.Date.AddDays(-daysSinceMonday);
    }

    private static TimeSpan Median(IReadOnlyList<TimeSpan> sorted)
    {
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
