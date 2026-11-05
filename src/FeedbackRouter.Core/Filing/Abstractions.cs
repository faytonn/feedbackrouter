using FeedbackRouter.Core.Domain;

namespace FeedbackRouter.Core.Filing;

public sealed record WorkItemRequest(
    int FeedbackItemId,
    Area Area,
    FeedbackType Type,
    int Severity,
    string Title,
    string Summary,
    string RawText);

public sealed record FilingResult(int? WorkItemId, string? Url, bool DryRun, string Project);

public interface IWorkItemFiler
{
    Task<FilingResult> FileAsync(WorkItemRequest request, CancellationToken cancellationToken);
}

/// <summary>Filing failed. The message is safe to show to a reviewer.</summary>
public sealed class WorkItemFilingException(string message, int? statusCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    public int? StatusCode { get; } = statusCode;
}
