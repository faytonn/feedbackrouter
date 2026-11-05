using System.Text.Json;
using FeedbackRouter.Core.Domain;

namespace FeedbackRouter.Core.Triage;

public sealed record ParsedTriage(
    Area Area,
    FeedbackType Type,
    int Severity,
    string Title,
    string Summary,
    double Confidence);

/// <summary>
/// Checks the model reply against the contract in the prompt. The rules here
/// are the schema: anything the prompt promises, this enforces.
/// </summary>
public static class TriageResponseParser
{
    public const int MaxTitleLength = 120;
    public const int MaxSummaryLength = 800;

    public static bool TryParse(string reply, out ParsedTriage? result, out string error)
    {
        result = null;
        error = string.Empty;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(reply);
        }
        catch (JsonException ex)
        {
            error = $"not JSON ({ex.Message})";
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "the reply must be a single JSON object.";
                return false;
            }

            if (!TryGetString(root, "area", out var areaText, ref error)
                || !TryGetString(root, "type", out var typeText, ref error)
                || !TryGetString(root, "title", out var title, ref error)
                || !TryGetString(root, "summary", out var summary, ref error))
            {
                return false;
            }

            if (!Labels.TryParseArea(areaText, out var area))
            {
                error = $"\"area\" is \"{areaText}\", which is not one of the listed areas.";
                return false;
            }

            if (!Labels.TryParseType(typeText, out var type))
            {
                error = $"\"type\" is \"{typeText}\", which must be Bug, Feature request or Question.";
                return false;
            }

            if (!root.TryGetProperty("severity", out var sevEl)
                || sevEl.ValueKind != JsonValueKind.Number
                || !sevEl.TryGetInt32(out var severity)
                || severity is < 1 or > 4)
            {
                error = "\"severity\" must be an integer from 1 to 4.";
                return false;
            }

            if (!root.TryGetProperty("confidence", out var confEl)
                || confEl.ValueKind != JsonValueKind.Number
                || confEl.GetDouble() is < 0 or > 1)
            {
                error = "\"confidence\" must be a number from 0 to 1.";
                return false;
            }

            title = title.Trim();
            summary = summary.Trim();

            if (title.Length == 0 || title.Length > MaxTitleLength || title.Contains('\n'))
            {
                error = $"\"title\" must be one non-empty line of at most {MaxTitleLength} characters.";
                return false;
            }

            if (summary.Length == 0 || summary.Length > MaxSummaryLength)
            {
                error = $"\"summary\" must be non-empty and at most {MaxSummaryLength} characters.";
                return false;
            }

            result = new ParsedTriage(area, type, severity, title, summary, confEl.GetDouble());
            return true;
        }
    }

    private static bool TryGetString(JsonElement root, string name, out string value, ref string error)
    {
        if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
        {
            value = el.GetString()!;
            return true;
        }

        value = string.Empty;
        error = $"\"{name}\" is missing or not a string.";
        return false;
    }
}
