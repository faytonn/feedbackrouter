using System.Diagnostics;

namespace FeedbackRouter.Core;

public static class Telemetry
{
    public const string SourceName = "FeedbackRouter";

    public static readonly ActivitySource Source = new(SourceName);
}
