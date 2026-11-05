using FeedbackRouter.Core.Domain;
using FeedbackRouter.Core.Filing;
using FeedbackRouter.Infra.Data;
using Microsoft.EntityFrameworkCore;

namespace FeedbackRouter.Api.Feedback;

public abstract record DecisionOutcome
{
    public sealed record Done : DecisionOutcome;

    public sealed record NotFound : DecisionOutcome;

    public sealed record Invalid(string Message) : DecisionOutcome;

    public sealed record Conflict(string Message) : DecisionOutcome;

    /// <summary>The decision was recorded but Azure DevOps did not create the work item. The item stays Approved.</summary>
    public sealed record FilingFailed(string Message) : DecisionOutcome;
}

/// <summary>
/// Applies a reviewer's decision. Every transition starts with a conditional
/// UPDATE on the item's status, so two requests racing on the same item
/// cannot both succeed, and an approved item is filed at most once.
/// </summary>
public sealed partial class DecisionService(
    FeedbackDbContext db,
    IWorkItemFiler filer,
    TimeProvider clock,
    ILogger<DecisionService> logger)
{
    /// <summary>How long a filing attempt holds the item before another approve may retry it.</summary>
    public static readonly TimeSpan FilingLease = TimeSpan.FromMinutes(2);

    private static readonly FeedbackStatus[] Reviewable = [FeedbackStatus.Proposed, FeedbackStatus.InReview];

    private static readonly FeedbackStatus[] Closable =
        [FeedbackStatus.New, FeedbackStatus.Proposed, FeedbackStatus.InReview];

    public Task<DecisionOutcome> DecideAsync(int id, DecisionRequest request, string reviewer, CancellationToken ct) =>
        request.Action switch
        {
            DecisionAction.Approve => ApproveAsync(id, request, reviewer, ct),
            DecisionAction.Merge => MergeAsync(id, request, reviewer, ct),
            DecisionAction.Dismiss => DismissAsync(id, request, reviewer, ct),
            _ => Task.FromResult<DecisionOutcome>(new DecisionOutcome.Invalid("action must be Approve, Merge or Dismiss.")),
        };

    private async Task<DecisionOutcome> ApproveAsync(int id, DecisionRequest request, string reviewer, CancellationToken ct)
    {
        var item = await db.FeedbackItems.AsNoTracking()
            .Include(i => i.Proposals)
            .Include(i => i.Decision)
            .SingleOrDefaultAsync(i => i.Id == id, ct);
        if (item is null)
        {
            return new DecisionOutcome.NotFound();
        }

        var proposal = item.Proposals.OrderByDescending(p => p.ProposedAt).ThenByDescending(p => p.Id).FirstOrDefault();
        if (proposal is null)
        {
            return new DecisionOutcome.Conflict("This item has no proposal yet, so there is nothing to approve.");
        }

        var area = request.Area ?? item.Decision?.FinalArea ?? proposal.Area;
        var type = request.Type ?? item.Decision?.FinalType ?? proposal.Type;
        var severity = request.Severity ?? item.Decision?.FinalSeverity ?? proposal.Severity;
        var title = (request.Title ?? item.Decision?.FinalTitle ?? proposal.Title).Trim();
        var summary = (request.Summary ?? item.Decision?.FinalSummary ?? proposal.Summary).Trim();

        if (severity is < 1 or > 4)
        {
            return new DecisionOutcome.Invalid("Severity must be from 1 to 4.");
        }

        if (title.Length == 0 || title.Length > 200 || title.Contains('\n'))
        {
            return new DecisionOutcome.Invalid("Title must be one line of 1 to 200 characters.");
        }

        if (summary.Length == 0 || summary.Length > 1000)
        {
            return new DecisionOutcome.Invalid("Summary must be 1 to 1000 characters.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var leaseExpired = now - FilingLease;

        var claimed = await InTransactionAsync(async () =>
        {
            // First approve: Proposed/InReview -> Approved. Retry after a failed
            // filing: Approved with no live claim. Anything else claims nothing.
            var rows = await db.FeedbackItems
                .Where(i => i.Id == id
                            && i.WorkItemLink == null
                            && (Reviewable.Contains(i.Status)
                                || (i.Status == FeedbackStatus.Approved
                                    && (i.FilingClaimedAt == null || i.FilingClaimedAt < leaseExpired))))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, FeedbackStatus.Approved)
                    .SetProperty(i => i.FilingClaimedAt, now), ct);
            if (rows == 0)
            {
                return false;
            }

            await UpsertDecisionAsync(id, DecisionAction.Approve, reviewer, now, request.Notes, d =>
            {
                d.FinalArea = area;
                d.FinalType = type;
                d.FinalSeverity = severity;
                d.FinalTitle = title;
                d.FinalSummary = summary;
            }, ct);
            return true;
        }, ct);

        if (!claimed)
        {
            return await ExplainConflictAsync(id, ct);
        }

        FilingResult result;
        try
        {
            result = await filer.FileAsync(new WorkItemRequest(id, area, type, severity, title, summary, item.RawText), ct);
        }
        catch (WorkItemFilingException ex)
        {
            LogFilingFailed(logger, ex, id, ex.StatusCode);
            await db.FeedbackItems.Where(i => i.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.FilingClaimedAt, (DateTime?)null), CancellationToken.None);
            return new DecisionOutcome.FilingFailed(ex.Message);
        }

        // The work item exists now; record it even if the caller has gone away.
        await InTransactionAsync(async () =>
        {
            db.WorkItemLinks.Add(new WorkItemLink
            {
                FeedbackItemId = id,
                AdoWorkItemId = result.WorkItemId,
                AdoUrl = result.Url,
                DryRun = result.DryRun,
                FiledAt = clock.GetUtcNow().UtcDateTime,
            });
            await db.SaveChangesAsync(CancellationToken.None);
            await db.FeedbackItems.Where(i => i.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, FeedbackStatus.Filed)
                    .SetProperty(i => i.FilingClaimedAt, (DateTime?)null), CancellationToken.None);
            return true;
        }, CancellationToken.None);

        LogFiled(logger, id, result.WorkItemId, result.DryRun);
        return new DecisionOutcome.Done();
    }

    private async Task<DecisionOutcome> MergeAsync(int id, DecisionRequest request, string reviewer, CancellationToken ct)
    {
        if (request.MergeIntoId is not { } targetId)
        {
            return new DecisionOutcome.Invalid("mergeIntoId is required to merge.");
        }

        if (targetId == id)
        {
            return new DecisionOutcome.Invalid("An item cannot be merged into itself.");
        }

        var target = await db.FeedbackItems.AsNoTracking()
            .Where(i => i.Id == targetId)
            .Select(i => new { i.Status, i.MergedIntoId })
            .SingleOrDefaultAsync(ct);
        if (target is null)
        {
            return new DecisionOutcome.Invalid($"Item {targetId} does not exist.");
        }

        if (target.Status == FeedbackStatus.Merged)
        {
            return new DecisionOutcome.Invalid($"Item {targetId} was itself merged into {target.MergedIntoId}. Merge into that one.");
        }

        if (target.Status == FeedbackStatus.Dismissed)
        {
            return new DecisionOutcome.Invalid($"Item {targetId} was dismissed.");
        }

        return await CloseAsync(id, FeedbackStatus.Merged, DecisionAction.Merge, targetId, reviewer, request.Notes, ct);
    }

    private Task<DecisionOutcome> DismissAsync(int id, DecisionRequest request, string reviewer, CancellationToken ct) =>
        CloseAsync(id, FeedbackStatus.Dismissed, DecisionAction.Dismiss, null, reviewer, request.Notes, ct);

    private async Task<DecisionOutcome> CloseAsync(
        int id, FeedbackStatus status, DecisionAction action, int? mergeIntoId, string reviewer, string? notes,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var done = await InTransactionAsync(async () =>
        {
            var rows = await db.FeedbackItems
                .Where(i => i.Id == id && Closable.Contains(i.Status))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, status)
                    .SetProperty(i => i.MergedIntoId, mergeIntoId), ct);
            if (rows == 0)
            {
                return false;
            }

            await UpsertDecisionAsync(id, action, reviewer, now, notes, _ => { }, ct);
            return true;
        }, ct);

        return done ? new DecisionOutcome.Done() : await ExplainConflictAsync(id, ct);
    }

    private async Task UpsertDecisionAsync(
        int id, DecisionAction action, string reviewer, DateTime now, string? notes,
        Action<TriageDecision> apply, CancellationToken ct)
    {
        var decision = await db.TriageDecisions.SingleOrDefaultAsync(d => d.FeedbackItemId == id, ct);
        if (decision is null)
        {
            decision = new TriageDecision { FeedbackItemId = id, ReviewerName = reviewer };
            db.TriageDecisions.Add(decision);
        }

        decision.ReviewerName = reviewer;
        decision.Action = action;
        decision.DecidedAt = now;
        decision.Notes = string.IsNullOrWhiteSpace(notes) ? decision.Notes : notes.Trim();
        apply(decision);
        await db.SaveChangesAsync(ct);
    }

    private async Task<DecisionOutcome> ExplainConflictAsync(int id, CancellationToken ct)
    {
        var current = await db.FeedbackItems.AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new { i.Status, i.MergedIntoId, WorkItemId = i.WorkItemLink!.AdoWorkItemId })
            .SingleOrDefaultAsync(ct);

        return current?.Status switch
        {
            null => new DecisionOutcome.NotFound(),
            FeedbackStatus.Filed => new DecisionOutcome.Conflict(current.WorkItemId is { } w
                ? $"Already filed as work item {w}."
                : "Already filed (dry run)."),
            FeedbackStatus.Approved => new DecisionOutcome.Conflict("Filing is already in progress for this item."),
            FeedbackStatus.Merged => new DecisionOutcome.Conflict($"Already merged into item {current.MergedIntoId}."),
            FeedbackStatus.Dismissed => new DecisionOutcome.Conflict("Already dismissed."),
            FeedbackStatus.New => new DecisionOutcome.Conflict("This item has no proposal yet."),
            _ => new DecisionOutcome.Conflict($"Item is {current.Status}."),
        };
    }

    private Task<bool> InTransactionAsync(Func<Task<bool>> work, CancellationToken ct)
    {
        // The SQL Server retrying strategy requires user transactions to run inside it.
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var ok = await work();
            await tx.CommitAsync(ct);
            return ok;
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Filing feedback {FeedbackId} failed with status {StatusCode}; item stays Approved")]
    private static partial void LogFilingFailed(ILogger logger, Exception ex, int feedbackId, int? statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Filed feedback {FeedbackId} as work item {WorkItemId} (dry run: {DryRun})")]
    private static partial void LogFiled(ILogger logger, int feedbackId, int? workItemId, bool dryRun);
}
