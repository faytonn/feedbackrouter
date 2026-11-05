using FeedbackRouter.Core.Triage;
using Microsoft.Extensions.AI;
using MeaiChatRole = Microsoft.Extensions.AI.ChatRole;

namespace FeedbackRouter.Infra.Llm;

/// <summary>Adapts any Microsoft.Extensions.AI chat client to the pipeline's model interface.</summary>
public sealed class ChatClientTriageModel(IChatClient client, string modelName) : ITriageModel
{
    private static readonly ChatOptions Options = new()
    {
        ResponseFormat = ChatResponseFormat.Json,
        Temperature = 0.1f,
    };

    public string ModelName => modelName;

    public async Task<string> CompleteAsync(IReadOnlyList<ChatTurn> conversation, CancellationToken cancellationToken)
    {
        var messages = conversation.Select(t => new ChatMessage(Map(t.Role), t.Text)).ToList();
        var response = await client.GetResponseAsync(messages, Options, cancellationToken);
        return response.Text;
    }

    private static MeaiChatRole Map(Core.Triage.ChatRole role) => role switch
    {
        Core.Triage.ChatRole.System => MeaiChatRole.System,
        Core.Triage.ChatRole.User => MeaiChatRole.User,
        Core.Triage.ChatRole.Assistant => MeaiChatRole.Assistant,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}

public sealed class EmbeddingGeneratorEmbedder(
    IEmbeddingGenerator<string, Embedding<float>> generator,
    string modelName) : ITextEmbedder
{
    public string ModelName => modelName;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var vector = await generator.GenerateVectorAsync(text, cancellationToken: cancellationToken);
        return vector.ToArray();
    }
}
