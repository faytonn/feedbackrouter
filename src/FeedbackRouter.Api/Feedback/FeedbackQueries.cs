using FeedbackRouter.Core.Domain;
using FeedbackRouter.Infra.AzureDevOps;
using FeedbackRouter.Infra.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FeedbackRouter.Api.Feedback;

public sealed class FeedbackQueries(FeedbackDbContext db, IOptions<AzureDevOpsOptions> ado)
{
    // Long enough that the hover text shows the whole first line; the table truncates visually.
    public const int UntitledLength = 300;

    public async Task<IReadOnlyList<QueueItemDto>> QueueAsync(
        FeedbackStatus? status, Area? area, CancellationToken ct)
    {
        var query = db.FeedbackItems.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(i => i.Status == s);
        }

        var rows = await query
            .Select(i => new
            {
                i.Id,
                i.RawText,
                i.SubmittedAt,
                i.Status,
                i.TriageError,
                i.ReviewerName,
                Decision = i.Decision,
                Link = i.WorkItemLink,
                Proposal = i.Proposals.OrderByDescending(p => p.ProposedAt).ThenByDescending(p => p.Id)
                    .Select(p => new { p.Area, p.Type, p.Severity, p.Title, Duplicates = p.DuplicateCandidates.Count })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var items = rows.Select(r =>
        {
            var finalTitle = r.Decision?.FinalTitle ?? r.Proposal?.Title;
            return new QueueItemDto(
                r.Id,
                finalTitle ?? Untitled(r.RawText),
                TitleIsProposed: finalTitle is not null,
                r.Decision?.FinalArea ?? r.Proposal?.Area,
                r.Decision?.FinalType ?? r.Proposal?.Type,
                r.Decision?.FinalSeverity ?? r.Proposal?.Severity,
                r.SubmittedAt,
                r.Proposal?.Duplicates ?? 0,
                r.Status,
                r.TriageError,
                r.ReviewerName,
                r.Link is { } l ? new WorkItemDto(l.AdoWorkItemId, l.AdoUrl, l.FiledAt, l.DryRun) : null);
        });

        if (area is { } a)
        {
            items = items.Where(i => i.Area == a);
        }

        // Severity first (1 is most severe, unknown last), then oldest first.
        return items
            .OrderBy(i => i.Severity ?? int.MaxValue)
            .ThenBy(i => i.SubmittedAt)
            .ThenBy(i => i.Id)
            .ToList();
    }

    public async Task<FeedbackDetailDto?> DetailAsync(int id, CancellationToken ct)
    {
        var item = await db.FeedbackItems.AsNoTracking()
            .Include(i => i.Proposals).ThenInclude(p => p.DuplicateCandidates)
            .Include(i => i.Decision)
            .Include(i => i.WorkItemLink)
            .AsSplitQuery()
            .SingleOrDefaultAsync(i => i.Id == id, ct);
        if (item is null)
        {
            return null;
        }

        var proposal = item.Proposals.OrderByDescending(p => p.ProposedAt).ThenByDescending(p => p.Id).FirstOrDefault();
        var duplicates = proposal is null
            ? []
            : await DuplicatesAsync(proposal.DuplicateCandidates, ct);

        var options = ado.Value;
        return new FeedbackDetailDto(
            item.Id,
            item.Source,
            item.SubmittedAt,
            item.RawText,
            item.SubmitterEmail,
            item.Status,
            item.MergedIntoId,
            item.TriageError,
            item is { Status: FeedbackStatus.InReview, ReviewerName: { } reviewer, ReviewActivityAt: { } activity }
                ? new ReviewDto(reviewer, activity, activity + ReviewService.IdleTimeout)
                : null,
            proposal is null
                ? null
                : new ProposalDto(proposal.Area, proposal.Type, proposal.Severity, proposal.Title, proposal.Summary,
                    proposal.Confidence, proposal.ProposedAt, proposal.ModelName, proposal.PromptVersion),
            duplicates,
            item.Decision is { } d
                ? new DecisionDto(d.Action, d.ReviewerName, d.DecidedAt, d.FinalArea, d.FinalType, d.FinalSeverity,
                    d.FinalTitle, d.FinalSummary, d.Notes)
                : null,
            item.WorkItemLink is { } l ? new WorkItemDto(l.AdoWorkItemId, l.AdoUrl, l.FiledAt, l.DryRun) : null,
            new FilingTargetDto(options.Organization, options.Project, options.DryRun));
    }

    private async Task<IReadOnlyList<DuplicateDto>> DuplicatesAsync(
        IReadOnlyList<DuplicateCandidate> candidates, CancellationToken ct)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var ids = candidates.Select(c => c.FeedbackItemId).ToList();
        var titles = await db.FeedbackItems.AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .Select(i => new
            {
                i.Id,
                i.Status,
                i.RawText,
                Title = i.Decision!.FinalTitle
                        ?? i.Proposals.OrderByDescending(p => p.ProposedAt).Select(p => p.Title).FirstOrDefault(),
            })
            .ToDictionaryAsync(i => i.Id, ct);

        return candidates
            .Where(c => titles.ContainsKey(c.FeedbackItemId))
            .OrderByDescending(c => c.Similarity)
            .Select(c =>
            {
                var t = titles[c.FeedbackItemId];
                return new DuplicateDto(c.FeedbackItemId, t.Title ?? Untitled(t.RawText), t.Status, c.Similarity);
            })
            .ToList();
    }

    public static string Untitled(string rawText)
    {
        var firstLine = rawText.Trim().Split('\n', 2)[0].Trim();
        return firstLine.Length <= UntitledLength ? firstLine : firstLine[..(UntitledLength - 1)].TrimEnd() + "…";
    }
}
