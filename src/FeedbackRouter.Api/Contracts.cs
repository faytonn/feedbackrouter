using FeedbackRouter.Core.Domain;

namespace FeedbackRouter.Api;

public sealed record SubmitFeedbackRequest(string? Text, string? Email, string? Source);

public sealed record SubmitFeedbackResponse(int Id, FeedbackStatus Status);

public sealed record QueueItemDto(
    int Id,
    string Title,
    bool TitleIsProposed,
    Area? Area,
    FeedbackType? Type,
    int? Severity,
    DateTime SubmittedAt,
    int DuplicateCount,
    FeedbackStatus Status,
    string? TriageError,
    string? ReviewerName,
    WorkItemDto? WorkItem);

public sealed record ProposalDto(
    Area Area,
    FeedbackType Type,
    int Severity,
    string Title,
    string Summary,
    double Confidence,
    DateTime ProposedAt,
    string ModelName,
    string PromptVersion);

public sealed record DuplicateDto(int Id, string Title, FeedbackStatus Status, double Similarity);

public sealed record DecisionDto(
    DecisionAction Action,
    string ReviewerName,
    DateTime DecidedAt,
    Area? FinalArea,
    FeedbackType? FinalType,
    int? FinalSeverity,
    string? FinalTitle,
    string? FinalSummary,
    string? Notes);

/// <summary>Who holds the item in InReview, and when the claim lapses without activity.</summary>
public sealed record ReviewDto(string ReviewerName, DateTime LastActivityAt, DateTime ExpiresAt);

public sealed record WorkItemDto(int? Id, string? Url, DateTime FiledAt, bool DryRun);

public sealed record FeedbackDetailDto(
    int Id,
    FeedbackSource Source,
    DateTime SubmittedAt,
    string RawText,
    string? SubmitterEmail,
    FeedbackStatus Status,
    int? MergedIntoId,
    string? TriageError,
    ReviewDto? Review,
    ProposalDto? Proposal,
    IReadOnlyList<DuplicateDto> Duplicates,
    DecisionDto? Decision,
    WorkItemDto? WorkItem,
    FilingTargetDto FilingTarget);

/// <summary>Where an approve would file, so the UI can say so before the reviewer clicks.</summary>
public sealed record FilingTargetDto(string Organization, string Project, bool DryRun);

/// <summary>The current reviewer; Name is null when sign-in is required and missing.</summary>
public sealed record MeDto(string? Name, string SignInUrl, string SignOutUrl);

public sealed record DecisionRequest(
    DecisionAction? Action,
    Area? Area,
    FeedbackType? Type,
    int? Severity,
    string? Title,
    string? Summary,
    string? Notes,
    int? MergeIntoId);

public sealed record StatsDto(
    IReadOnlyList<CountDto> ByArea,
    IReadOnlyList<CountDto> ByStatus,
    IReadOnlyList<DayCountDto> PerDay,
    IReadOnlyList<CountDto> TimeToTriageBuckets,
    int Open,
    int FiledThisWeek,
    double? MedianTimeToTriageSeconds,
    double? DuplicateRate,
    int Decided);

public sealed record CountDto(string Key, int Count);

public sealed record DayCountDto(DateOnly Day, int Count);
