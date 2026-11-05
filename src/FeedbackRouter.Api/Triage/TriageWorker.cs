using System.Diagnostics;
using System.Threading.Channels;
using FeedbackRouter.Core;
using FeedbackRouter.Core.Domain;
using FeedbackRouter.Core.Triage;
using FeedbackRouter.Infra.Data;
using FeedbackRouter.Infra.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FeedbackRouter.Api.Triage;

/// <summary>Lets endpoints wake the worker as soon as new items arrive instead of waiting for the next poll.</summary>
public sealed class TriageSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify() => _channel.Writer.TryWrite(true);

    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await _channel.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Poll interval elapsed.
        }
    }
}

public sealed class TriageOptions
{
    public const string Section = "Triage";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);

    public int BatchSize { get; set; } = 10;

    /// <summary>Runs per item before the worker stops retrying it.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Wait after a failed run, multiplied by the number of attempts so far.</summary>
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// Picks up items in New, runs the triage pipeline and stores the proposal.
/// Assumes a single API replica; more replicas would need a claim on each item.
/// </summary>
public sealed partial class TriageWorker(
    IServiceScopeFactory scopes,
    TriageSignal signal,
    IOptions<TriageOptions> options,
    IOptions<LlmOptions> llm,
    TimeProvider clock,
    ILogger<TriageWorker> logger) : BackgroundService
{
    private readonly TriageOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!llm.Value.IsConfigured)
        {
            LogNotConfigured(logger);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await RunBatchAsync(stoppingToken);
                if (processed == _options.BatchSize)
                {
                    continue;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogBatchFailed(logger, ex);
            }

            await signal.WaitAsync(_options.PollInterval, stoppingToken);
        }
    }

    public async Task<int> RunBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
        var pipeline = scope.ServiceProvider.GetRequiredService<TriagePipeline>();
        var now = clock.GetUtcNow().UtcDateTime;

        var candidates = await db.FeedbackItems
            .Where(i => i.Status == FeedbackStatus.New && i.TriageAttempts < _options.MaxAttempts)
            .OrderBy(i => i.SubmittedAt)
            .ThenBy(i => i.Id)
            .Take(_options.BatchSize * 4)
            .ToListAsync(ct);
        var due = candidates
            .Where(i => i.LastTriageAttemptAt is null
                        || i.LastTriageAttemptAt <= now - (_options.RetryBackoff * i.TriageAttempts))
            .Take(_options.BatchSize)
            .ToList();
        if (due.Count == 0)
        {
            return 0;
        }

        var pool = await LoadPoolAsync(db, ct);
        foreach (var item in due)
        {
            var proposal = await TriageOneAsync(db, pipeline, item, pool, ct);
            if (proposal is not null && item.Embedding is not null)
            {
                // Later items in this batch can be matched against this one.
                pool.Add(new PoolEntry(item.Id, proposal.Title, item.Embedding));
            }
        }

        return due.Count;
    }

    private async Task<TriageProposal?> TriageOneAsync(
        FeedbackDbContext db, TriagePipeline pipeline, FeedbackItem item, List<PoolEntry> pool, CancellationToken ct)
    {
        using var activity = Telemetry.Source.StartActivity("triage.run");
        activity?.SetTag("feedback.id", item.Id);
        activity?.SetTag("triage.attempt", item.TriageAttempts + 1);

        item.TriageAttempts++;
        item.LastTriageAttemptAt = clock.GetUtcNow().UtcDateTime;
        TriageProposal? proposal = null;

        try
        {
            var outcome = await pipeline.RunAsync(item, pool, ct);
            item.Embedding = outcome.Embedding;

            switch (outcome)
            {
                case TriageOutcome.Proposed p:
                    proposal = p.Proposal;
                    item.Proposals.Add(proposal);
                    item.Status = FeedbackStatus.Proposed;
                    item.TriageError = null;
                    activity?.SetTag("triage.area", proposal.Area.ToString());
                    activity?.SetTag("triage.duplicates", proposal.DuplicateCandidates.Count);
                    LogProposed(logger, item.Id, proposal.Area, proposal.Severity, proposal.DuplicateCandidates.Count);
                    break;
                case TriageOutcome.Invalid invalid:
                    item.TriageError = $"Model reply was invalid after {invalid.ModelCalls} calls: {invalid.Error}";
                    activity?.SetStatus(ActivityStatusCode.Error, "invalid model reply");
                    LogInvalid(logger, item.Id, invalid.ModelCalls, invalid.Error);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            item.TriageError = Truncate($"Triage failed: {ex.Message}", 1000);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogFailed(logger, ex, item.Id);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A reviewer merged or dismissed the item while the model was running. Their decision wins.
            LogSuperseded(logger, item.Id);
            db.ChangeTracker.Clear();
            return null;
        }

        return proposal;
    }

    private static async Task<List<PoolEntry>> LoadPoolAsync(FeedbackDbContext db, CancellationToken ct)
    {
        var rows = await db.FeedbackItems.AsNoTracking()
            .Where(i => DuplicateDetector.EligibleStatuses.Contains(i.Status) && i.Embedding != null)
            .Select(i => new
            {
                i.Id,
                i.Embedding,
                Title = i.Decision!.FinalTitle
                        ?? i.Proposals.OrderByDescending(p => p.ProposedAt).Select(p => p.Title).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return rows
            .Where(r => r.Title is not null)
            .Select(r => new PoolEntry(r.Id, r.Title!, r.Embedding!))
            .ToList();
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Llm is not configured; triage worker is off and items stay in New")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Triage batch failed")]
    private static partial void LogBatchFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Proposed feedback {FeedbackId}: area {Area}, severity {Severity}, {DuplicateCount} duplicate candidates")]
    private static partial void LogProposed(ILogger logger, int feedbackId, Area area, int severity, int duplicateCount);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Feedback {FeedbackId} left in New: model reply invalid after {Calls} calls: {Error}")]
    private static partial void LogInvalid(ILogger logger, int feedbackId, int calls, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Triage of feedback {FeedbackId} failed")]
    private static partial void LogFailed(ILogger logger, Exception ex, int feedbackId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Feedback {FeedbackId} was decided during triage; proposal discarded")]
    private static partial void LogSuperseded(ILogger logger, int feedbackId);
}
