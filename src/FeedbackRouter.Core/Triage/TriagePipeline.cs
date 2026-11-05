using FeedbackRouter.Core.Domain;

namespace FeedbackRouter.Core.Triage;

public abstract record TriageOutcome(float[] Embedding)
{
    public sealed record Proposed(float[] Embedding, TriageProposal Proposal) : TriageOutcome(Embedding);

    /// <summary>The model never produced a valid reply. The item stays in New.</summary>
    public sealed record Invalid(float[] Embedding, string Error, int ModelCalls) : TriageOutcome(Embedding);
}

/// <summary>
/// Embed, find duplicate candidates, ask the model, validate. The pipeline
/// only proposes; it never changes an item's status and never approves.
/// </summary>
public sealed class TriagePipeline(ITextEmbedder embedder, ITriageModel model, TriagePrompt prompt, TimeProvider clock)
{
    public const int MaxModelCalls = 2;

    public async Task<TriageOutcome> RunAsync(
        FeedbackItem item,
        IEnumerable<PoolEntry> pool,
        CancellationToken cancellationToken)
    {
        var embedding = item.Embedding ?? await embedder.EmbedAsync(item.RawText, cancellationToken);
        var candidates = DuplicateDetector.FindCandidates(item.Id, embedding, pool);

        var conversation = new List<ChatTurn>
        {
            new(ChatRole.System, prompt.SystemText),
            new(ChatRole.User, TriagePrompt.BuildUserMessage(item.RawText, candidates)),
        };

        var error = string.Empty;
        for (var call = 1; call <= MaxModelCalls; call++)
        {
            var reply = await model.CompleteAsync(conversation, cancellationToken);
            if (TriageResponseParser.TryParse(reply, out var parsed, out error))
            {
                return new TriageOutcome.Proposed(embedding, ToProposal(item.Id, parsed!, candidates));
            }

            conversation.Add(new ChatTurn(ChatRole.Assistant, reply));
            conversation.Add(new ChatTurn(ChatRole.User, TriagePrompt.BuildRetryMessage(error)));
        }

        return new TriageOutcome.Invalid(embedding, error, MaxModelCalls);
    }

    private TriageProposal ToProposal(int itemId, ParsedTriage parsed, IReadOnlyList<ScoredCandidate> candidates) => new()
    {
        FeedbackItemId = itemId,
        Area = parsed.Area,
        Type = parsed.Type,
        Severity = parsed.Severity,
        Title = parsed.Title,
        Summary = parsed.Summary,
        Confidence = parsed.Confidence,
        ProposedAt = clock.GetUtcNow().UtcDateTime,
        ModelName = model.ModelName,
        PromptVersion = prompt.Version,
        DuplicateCandidates = candidates
            .Select(c => new DuplicateCandidate { FeedbackItemId = c.FeedbackItemId, Similarity = Math.Round(c.Similarity, 4) })
            .ToList(),
    };
}
