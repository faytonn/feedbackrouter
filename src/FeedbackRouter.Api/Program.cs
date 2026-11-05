using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using FeedbackRouter.Api;
using FeedbackRouter.Api.Feedback;
using FeedbackRouter.Api.Triage;
using FeedbackRouter.Core;
using FeedbackRouter.Core.Filing;
using FeedbackRouter.Core.Triage;
using FeedbackRouter.Infra.AzureDevOps;
using FeedbackRouter.Infra.Data;
using FeedbackRouter.Infra.Llm;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Database
builder.Services.AddDbContext<FeedbackDbContext>(o => o.UseSqlServer(
    config.GetConnectionString("FeedbackRouter"),
    sql => sql.EnableRetryOnFailure()));

// LLM: provider chosen by configuration, wrapped with tracing and logging.
builder.Services.Configure<LlmOptions>(config.GetSection(LlmOptions.Section));
var llm = config.GetSection(LlmOptions.Section).Get<LlmOptions>() ?? new LlmOptions();
if (llm.IsConfigured)
{
    builder.Services.AddChatClient(_ => LlmClientFactory.CreateChatClient(llm)).UseOpenTelemetry().UseLogging();
    builder.Services.AddEmbeddingGenerator(_ => LlmClientFactory.CreateEmbeddingGenerator(llm)).UseOpenTelemetry().UseLogging();
    builder.Services.AddScoped<ITriageModel>(sp => new ChatClientTriageModel(sp.GetRequiredService<IChatClient>(), llm.ChatModel));
    builder.Services.AddScoped<ITextEmbedder>(sp => new EmbeddingGeneratorEmbedder(
        sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(), llm.EmbeddingModel));
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => TriagePrompt.Load(config["Triage:PromptVersion"] ?? TriagePrompt.CurrentVersion));
builder.Services.AddScoped<TriagePipeline>();
builder.Services.Configure<TriageOptions>(config.GetSection(TriageOptions.Section));
builder.Services.AddSingleton<TriageSignal>();
builder.Services.AddHostedService<TriageWorker>();

// Azure DevOps
builder.Services.Configure<AzureDevOpsOptions>(config.GetSection(AzureDevOpsOptions.Section));
builder.Services.AddHttpClient<IWorkItemFiler, AzureDevOpsWorkItemFiler>(c => c.Timeout = TimeSpan.FromSeconds(30));

builder.Services.AddScoped<FeedbackQueries>();
builder.Services.AddScoped<DecisionService>();
builder.Services.AddScoped<StatsQuery>();
builder.Services.AddScoped<ReviewerIdentity>();

// HTTP
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // Container Apps terminates TLS in its ingress proxy and appends the client
    // address; ForwardLimit = 1 reads only the entry that proxy added.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(Endpoints.SubmitRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.GetValue("RateLimit:SubmitPerMinute", 10),
            Window = TimeSpan.FromMinutes(1),
        }));
});

// Telemetry: Application Insights when a connection string is present, console otherwise.
var otel = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("feedbackrouter"))
    .WithTracing(t => t
        .AddSource(Telemetry.SourceName)
        .AddSource("Experimental.Microsoft.Extensions.AI")
        .AddAspNetCoreInstrumentation(a => a.Filter = http => http.Request.Path != "/healthz")
        .AddHttpClientInstrumentation());
if (!string.IsNullOrWhiteSpace(config["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    otel.UseAzureMonitor();
}
else if (config.GetValue("Telemetry:ConsoleExporter", true))
{
    otel.WithTracing(t => t.AddConsoleExporter());
    builder.Logging.AddOpenTelemetry(l =>
    {
        l.IncludeScopes = true;
        l.AddConsoleExporter();
    });
}

var app = builder.Build();

if (config.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<FeedbackDbContext>().Database.MigrateAsync();
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapFeedbackRouter();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
