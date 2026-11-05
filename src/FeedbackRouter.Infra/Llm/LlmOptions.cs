using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace FeedbackRouter.Infra.Llm;

public enum LlmProvider
{
    AzureOpenAI,
    OpenAI,
}

public sealed class LlmOptions
{
    public const string Section = "Llm";

    public LlmProvider Provider { get; set; } = LlmProvider.AzureOpenAI;

    /// <summary>Azure OpenAI resource endpoint, e.g. https://my-resource.openai.azure.com. Ignored for OpenAI.</summary>
    public string? Endpoint { get; set; }

    public string? ApiKey { get; set; }

    /// <summary>Model name for OpenAI, deployment name for Azure OpenAI.</summary>
    public string ChatModel { get; set; } = "gpt-4o-mini";

    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && (Provider == LlmProvider.OpenAI || !string.IsNullOrWhiteSpace(Endpoint));
}

public static class LlmClientFactory
{
    /// <summary>
    /// Azure OpenAI exposes an OpenAI-compatible v1 API at {endpoint}/openai/v1/,
    /// so one client type serves both providers; only the endpoint differs.
    /// </summary>
    public static OpenAIClient CreateOpenAIClient(LlmOptions options)
    {
        if (!options.IsConfigured)
        {
            throw new InvalidOperationException("Llm is not configured: set Llm:ApiKey, and Llm:Endpoint for Azure OpenAI.");
        }

        var clientOptions = new OpenAIClientOptions();
        if (options.Provider == LlmProvider.AzureOpenAI)
        {
            clientOptions.Endpoint = new Uri(options.Endpoint!.TrimEnd('/') + "/openai/v1/");
        }

        return new OpenAIClient(new ApiKeyCredential(options.ApiKey!), clientOptions);
    }

    public static IChatClient CreateChatClient(LlmOptions options) =>
        CreateOpenAIClient(options).GetChatClient(options.ChatModel).AsIChatClient();

    public static IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(LlmOptions options) =>
        CreateOpenAIClient(options).GetEmbeddingClient(options.EmbeddingModel).AsIEmbeddingGenerator();
}
