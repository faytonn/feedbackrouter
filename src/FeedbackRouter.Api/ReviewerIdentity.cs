namespace FeedbackRouter.Api;

/// <summary>
/// Who is making a decision. There is no sign-in yet, so every decision is
/// recorded under the configured reviewer name.
/// </summary>
public sealed class ReviewerIdentity(IConfiguration config)
{
    public string Name => config["Reviewer:Name"] is { Length: > 0 } name ? name : "reviewer";
}
