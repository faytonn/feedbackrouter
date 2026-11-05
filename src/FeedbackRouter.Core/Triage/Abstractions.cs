namespace FeedbackRouter.Core.Triage;

public interface ITextEmbedder
{
    string ModelName { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken);
}

public interface ITriageModel
{
    string ModelName { get; }

    Task<string> CompleteAsync(IReadOnlyList<ChatTurn> conversation, CancellationToken cancellationToken);
}

public enum ChatRole
{
    System,
    User,
    Assistant,
}

public sealed record ChatTurn(ChatRole Role, string Text);
