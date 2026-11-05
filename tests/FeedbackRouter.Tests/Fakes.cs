using FeedbackRouter.Core.Triage;

namespace FeedbackRouter.Tests;

/// <summary>Returns the scripted replies in order and records every conversation it was sent.</summary>
public sealed class FakeTriageModel(params string[] replies) : ITriageModel
{
    private readonly Queue<string> _replies = new(replies);

    public string ModelName => "fake-model";

    public int Calls => Conversations.Count;

    public List<IReadOnlyList<ChatTurn>> Conversations { get; } = [];

    public Task<string> CompleteAsync(IReadOnlyList<ChatTurn> conversation, CancellationToken cancellationToken)
    {
        Conversations.Add(conversation.ToList());
        return Task.FromResult(_replies.Dequeue());
    }
}

public sealed class FakeEmbedder(float[] vector) : ITextEmbedder
{
    public string ModelName => "fake-embedder";

    public int Calls { get; private set; }

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(vector);
    }
}

public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
