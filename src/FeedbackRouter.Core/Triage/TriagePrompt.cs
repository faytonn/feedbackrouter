using System.Reflection;
using System.Text;

namespace FeedbackRouter.Core.Triage;

public sealed class TriagePrompt
{
    public const string CurrentVersion = "triage.v1";

    private TriagePrompt(string version, string systemText)
    {
        Version = version;
        SystemText = systemText;
    }

    public string Version { get; }

    public string SystemText { get; }

    /// <summary>Loads Prompts/{version}.md, which is embedded in this assembly.</summary>
    public static TriagePrompt Load(string version = CurrentVersion)
    {
        var assembly = typeof(TriagePrompt).Assembly;
        var resourceName = $"FeedbackRouter.Core.Prompts.{version}.md";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Prompt '{version}' is not embedded (looked for {resourceName}).");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return new TriagePrompt(version, reader.ReadToEnd());
    }

    public static string BuildUserMessage(string feedbackText, IReadOnlyList<ScoredCandidate> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Feedback:");
        sb.AppendLine("<<<");
        sb.AppendLine(feedbackText.Trim());
        sb.AppendLine(">>>");
        sb.AppendLine();

        if (candidates.Count == 0)
        {
            sb.AppendLine("Possible duplicates: none.");
        }
        else
        {
            sb.AppendLine("Possible duplicates:");
            foreach (var c in candidates)
            {
                sb.AppendLine($"- {c.Title}");
            }
        }

        return sb.ToString();
    }

    public static string BuildRetryMessage(string error) =>
        $"That reply was not valid: {error} Reply again with only the JSON object described in the instructions.";
}
