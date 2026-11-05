using FeedbackRouter.Core.Domain;

namespace FeedbackRouter.Infra.AzureDevOps;

public sealed class AzureDevOpsOptions
{
    public const string Section = "AzureDevOps";

    public string BaseUrl { get; set; } = "https://dev.azure.com";

    public string Organization { get; set; } = string.Empty;

    public string Project { get; set; } = string.Empty;

    public string? Pat { get; set; }

    /// <summary>Log the request instead of sending it. On by default so the app runs without a PAT.</summary>
    public bool DryRun { get; set; } = true;

    /// <summary>Work item type per feedback type. The defaults match the Agile process.</summary>
    public Dictionary<FeedbackType, string> WorkItemTypes { get; set; } = new()
    {
        [FeedbackType.Bug] = "Bug",
        [FeedbackType.FeatureRequest] = "User Story",
        [FeedbackType.Question] = "Issue",
    };

    /// <summary>Optional area path per area. Areas without an entry get the project's default area path.</summary>
    public Dictionary<Area, string> AreaPaths { get; set; } = [];

    public int MaxRetries { get; set; } = 3;

    /// <summary>Upper bound for any single wait between retries, whatever Retry-After says.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);
}
