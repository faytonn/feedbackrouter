namespace FeedbackRouter.Core.Domain;

public sealed class FeedbackItem
{
    public int Id { get; set; }
    public FeedbackSource Source { get; set; }
    public DateTime SubmittedAt { get; set; }
    public required string RawText { get; set; }
    public string? SubmitterEmail { get; set; }
    public FeedbackStatus Status { get; set; } = FeedbackStatus.New;
    public float[]? Embedding { get; set; }
    public int? MergedIntoId { get; set; }

    // Triage bookkeeping. A failed run leaves the item in New with the error
    // recorded, and the worker backs off before trying again.
    public int TriageAttempts { get; set; }
    public DateTime? LastTriageAttemptAt { get; set; }
    public string? TriageError { get; set; }

    // Set while a request is filing this item in Azure DevOps, so a second
    // approve cannot start a second filing.
    public DateTime? FilingClaimedAt { get; set; }

    public List<TriageProposal> Proposals { get; set; } = [];
    public TriageDecision? Decision { get; set; }
    public WorkItemLink? WorkItemLink { get; set; }
}

public sealed class TriageProposal
{
    public int Id { get; set; }
    public int FeedbackItemId { get; set; }
    public Area Area { get; set; }
    public FeedbackType Type { get; set; }
    public int Severity { get; set; }
    public required string Title { get; set; }
    public required string Summary { get; set; }
    public double Confidence { get; set; }
    public DateTime ProposedAt { get; set; }
    public required string ModelName { get; set; }
    public required string PromptVersion { get; set; }
    public List<DuplicateCandidate> DuplicateCandidates { get; set; } = [];
}

public sealed class DuplicateCandidate
{
    public int FeedbackItemId { get; set; }
    public double Similarity { get; set; }
}

public sealed class TriageDecision
{
    public int FeedbackItemId { get; set; }
    public required string ReviewerName { get; set; }
    public DateTime DecidedAt { get; set; }
    public DecisionAction Action { get; set; }
    public Area? FinalArea { get; set; }
    public FeedbackType? FinalType { get; set; }
    public int? FinalSeverity { get; set; }
    public string? FinalTitle { get; set; }
    public string? FinalSummary { get; set; }
    public string? Notes { get; set; }
}

public sealed class WorkItemLink
{
    public int FeedbackItemId { get; set; }

    // Null when the item was filed in dry-run mode: nothing was created in
    // Azure DevOps, so there is no id or URL to store.
    public int? AdoWorkItemId { get; set; }
    public string? AdoUrl { get; set; }
    public DateTime FiledAt { get; set; }
    public bool DryRun { get; set; }
}
