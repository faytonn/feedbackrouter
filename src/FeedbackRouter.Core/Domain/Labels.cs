namespace FeedbackRouter.Core.Domain;

/// <summary>
/// The human-readable names used in the prompt, the API and Azure DevOps.
/// Enum member names stay code-friendly; these are what people read.
/// </summary>
public static class Labels
{
    private static readonly Dictionary<Area, string> AreaNames = new()
    {
        [Area.Editor] = "Editor",
        [Area.Debugger] = "Debugger",
        [Area.Build] = "Build",
        [Area.Git] = "Git",
        [Area.Extensions] = "Extensions",
        [Area.LicensingAndSubscriptions] = "Licensing & Subscriptions",
        [Area.Docs] = "Docs",
        [Area.Other] = "Other",
    };

    private static readonly Dictionary<FeedbackType, string> TypeNames = new()
    {
        [FeedbackType.Bug] = "Bug",
        [FeedbackType.FeatureRequest] = "Feature request",
        [FeedbackType.Question] = "Question",
    };

    public static string Of(Area area) => AreaNames[area];

    public static string Of(FeedbackType type) => TypeNames[type];

    public static bool TryParseArea(string? text, out Area area) => TryParse(AreaNames, text, out area);

    public static bool TryParseType(string? text, out FeedbackType type) => TryParse(TypeNames, text, out type);

    private static bool TryParse<T>(Dictionary<T, string> names, string? text, out T value)
        where T : struct, Enum
    {
        foreach (var (key, name) in names)
        {
            if (string.Equals(name, text?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                value = key;
                return true;
            }
        }

        value = default;
        return false;
    }
}
