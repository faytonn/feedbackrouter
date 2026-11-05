using System.Text.Json;
using FeedbackRouter.Api.Triage;
using FeedbackRouter.Core.Domain;
using FeedbackRouter.Core.Triage;
using FeedbackRouter.Infra.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FeedbackRouter.Api;

public sealed class DemoOptions
{
    public const string Section = "Demo";

    /// <summary>Public demo: seed on first start, reset to the seed nightly, show a banner, never file.</summary>
    public bool Enabled { get; set; }

    /// <summary>Time of day, in UTC, when the data is reset.</summary>
    public TimeOnly ResetAtUtc { get; set; } = new(3, 0);
}

/// <summary>Loads seed/feedback.json. Used by POST /api/seed in Development and by the demo reset.</summary>
public sealed class SeedImporter(FeedbackDbContext db, TriageSignal signal, TimeProvider clock, IConfiguration config, IHostEnvironment env)
{
    private sealed record SeedItem(string Text, string? Email, double AgeHours, SeedProposal? Proposal);

    /// <summary>
    /// A proposal generated ahead of time and stored with the seed, so the demo shows
    /// proposals without live model calls. Reply follows the triage prompt's JSON contract.
    /// </summary>
    private sealed record SeedProposal(string Model, string PromptVersion, JsonElement Reply);

    public string SeedPath => Path.GetFullPath(config["Seed:Path"] ?? "../../seed/feedback.json", env.ContentRootPath);

    public Task<bool> HasImportedAsync(CancellationToken ct) =>
        db.FeedbackItems.AnyAsync(i => i.Source == FeedbackSource.Import, ct);

    /// <summary>
    /// Adds the seed items, aged relative to now. Items with a stored proposal arrive as
    /// Proposed; it is validated by the same parser as live replies. Throws if the file is missing.
    /// </summary>
    public async Task<int> ImportAsync(CancellationToken ct)
    {
        await using var stream = File.OpenRead(SeedPath);
        var seed = await JsonSerializer.DeserializeAsync<List<SeedItem>>(stream, JsonSerializerOptions.Web, ct) ?? [];
        var now = clock.GetUtcNow().UtcDateTime;

        db.FeedbackItems.AddRange(seed.Select((s, index) =>
        {
            var item = new FeedbackItem
            {
                RawText = s.Text.Trim(),
                SubmitterEmail = s.Email,
                Source = FeedbackSource.Import,
                SubmittedAt = now.AddHours(-s.AgeHours),
            };
            if (s.Proposal is { } p)
            {
                if (!TriageResponseParser.TryParse(p.Reply.GetRawText(), out var parsed, out var error))
                {
                    throw new InvalidDataException($"Seed item {index + 1} has an invalid proposal: {error}");
                }

                item.Status = FeedbackStatus.Proposed;
                item.Proposals.Add(new TriageProposal
                {
                    Area = parsed!.Area,
                    Type = parsed.Type,
                    Severity = parsed.Severity,
                    Title = parsed.Title,
                    Summary = parsed.Summary,
                    Confidence = parsed.Confidence,
                    ProposedAt = now,
                    ModelName = p.Model,
                    PromptVersion = p.PromptVersion,
                });
            }

            return item;
        }));
        await db.SaveChangesAsync(ct);
        signal.Notify();
        return seed.Count;
    }

    /// <summary>Deletes every item, with its proposals, decisions and links, then imports the seed again.</summary>
    public async Task<int> ResetAsync(CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
        {
            // Also restarts the id sequences, so the seed comes back as #1 to #40.
            await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"FeedbackItems\" RESTART IDENTITY CASCADE", ct);
        }
        else
        {
            // Child tables cascade from FeedbackItems.
            await db.FeedbackItems.ExecuteDeleteAsync(ct);
        }

        db.ChangeTracker.Clear();
        return await ImportAsync(ct);
    }
}

/// <summary>In demo mode: imports the seed into an empty database, then resets it once a day.</summary>
public sealed partial class DemoResetService(
    IServiceScopeFactory scopes,
    IOptions<DemoOptions> options,
    TimeProvider clock,
    ILogger<DemoResetService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        await RunAsync(reset: false, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(UntilNextReset(clock.GetUtcNow().UtcDateTime, options.Value.ResetAtUtc), clock, stoppingToken);
            await RunAsync(reset: true, stoppingToken);
        }
    }

    public static TimeSpan UntilNextReset(DateTime nowUtc, TimeOnly resetAt)
    {
        var next = nowUtc.Date + resetAt.ToTimeSpan();
        if (next <= nowUtc)
        {
            next = next.AddDays(1);
        }

        return next - nowUtc;
    }

    private async Task RunAsync(bool reset, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var seed = scope.ServiceProvider.GetRequiredService<SeedImporter>();
            var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
            if (reset)
            {
                LogReset(logger, await seed.ResetAsync(ct));
            }
            else if (!await db.FeedbackItems.AnyAsync(ct))
            {
                LogReset(logger, await seed.ImportAsync(ct));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo data reset to {Count} seed items")]
    private static partial void LogReset(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Demo data reset failed")]
    private static partial void LogFailed(ILogger logger, Exception ex);
}
