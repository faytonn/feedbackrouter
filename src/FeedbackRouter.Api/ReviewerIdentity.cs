namespace FeedbackRouter.Api;

/// <summary>
/// Who is reviewing. In Azure, Container Apps built-in sign-in authenticates
/// the user and passes their name in <see cref="PrincipalNameHeader"/>; the
/// platform strips that header from incoming requests, so the app can trust
/// it. Only enable Auth:RequireSignIn behind that sign-in, never on its own.
/// Locally, sign-in is off and decisions use Reviewer:Name.
/// </summary>
public sealed class ReviewerIdentity(IHttpContextAccessor http, IConfiguration config)
{
    public const string PrincipalNameHeader = "X-MS-CLIENT-PRINCIPAL-NAME";

    public bool RequireSignIn => config.GetValue("Auth:RequireSignIn", true);

    /// <summary>The signed-in reviewer, or null when sign-in is required and missing.</summary>
    public string? Name
    {
        get
        {
            if (!RequireSignIn)
            {
                // Without the platform sign-in in front, the header could come from anyone.
                return config["Reviewer:Name"] is { Length: > 0 } name ? name : "reviewer";
            }

            var header = http.HttpContext?.Request.Headers[PrincipalNameHeader].ToString();
            return string.IsNullOrWhiteSpace(header) ? null : header.Trim();
        }
    }
}
