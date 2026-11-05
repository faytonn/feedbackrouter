namespace FeedbackRouter.Core.Domain;

public enum FeedbackSource
{
    Form,
    Api,
    Import,
}

public enum FeedbackStatus
{
    New,
    Proposed,
    InReview,
    Approved,
    Filed,
    Merged,
    Dismissed,
}

public enum Area
{
    Editor,
    Debugger,
    Build,
    Git,
    Extensions,
    LicensingAndSubscriptions,
    Docs,
    Other,
}

public enum FeedbackType
{
    Bug,
    FeatureRequest,
    Question,
}

public enum DecisionAction
{
    Approve,
    Merge,
    Dismiss,
}
