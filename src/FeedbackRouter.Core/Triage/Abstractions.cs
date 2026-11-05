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

/// <summary>Thrown instead of calling the model once the daily cap on model calls is reached.</summary>
public sealed class LlmBudgetExhaustedException(int cap)
    : Exception($"The daily cap of {cap} model calls is reached.")
{
    public int Cap { get; } = cap;
}
