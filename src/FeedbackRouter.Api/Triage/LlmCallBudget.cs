using FeedbackRouter.Core.Domain;
using FeedbackRouter.Core.Triage;
using FeedbackRouter.Infra.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FeedbackRouter.Api.Triage;

/// <summary>
/// A global cap on triage model calls per UTC day, counted in the database so
/// restarts do not reset it. Each reservation uses its own DbContext, so it
/// never saves the worker's half-finished changes.
/// </summary>
public sealed class LlmCallBudget(IServiceScopeFactory scopes, IOptions<TriageOptions> options, TimeProvider clock)
{
    public int Cap => options.Value.DailyLlmCallCap;

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<int> UsedTodayAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
        var today = Today;
        return await db.LlmUsage.Where(u => u.Day == today).Select(u => u.Calls).SingleOrDefaultAsync(ct);
    }

    public async Task<bool> HasRemainingAsync(CancellationToken ct) => await UsedTodayAsync(ct) < Cap;

    /// <summary>Counts one call if the cap allows it. Returns false when the cap is reached.</summary>
    public async Task<bool> TryReserveAsync(CancellationToken ct)
    {
        if (Cap <= 0)
        {
            return false;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
        var today = Today;
        var cap = Cap;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var rows = await db.LlmUsage
                .Where(u => u.Day == today && u.Calls < cap)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Calls, u => u.Calls + 1), ct);
            if (rows == 1)
            {
                return true;
            }

            if (await db.LlmUsage.AnyAsync(u => u.Day == today, ct))
            {
                return false;
            }

            // First call of the day. Another writer may insert the row first; then loop and increment it.
            db.LlmUsage.Add(new LlmUsage { Day = today, Calls = 1 });
            try
            {
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
            }
        }

        return false;
    }

    public async Task ReserveOrThrowAsync(CancellationToken ct)
    {
        if (!await TryReserveAsync(ct))
        {
            throw new LlmBudgetExhaustedException(Cap);
        }
    }
}

/// <summary>Counts each chat call against the daily cap before making it.</summary>
public sealed class BudgetedTriageModel(ITriageModel inner, LlmCallBudget budget) : ITriageModel
{
    public string ModelName => inner.ModelName;

    public async Task<string> CompleteAsync(IReadOnlyList<ChatTurn> conversation, CancellationToken cancellationToken)
    {
        await budget.ReserveOrThrowAsync(cancellationToken);
        return await inner.CompleteAsync(conversation, cancellationToken);
    }
}

/// <summary>Counts each embedding call against the daily cap before making it.</summary>
public sealed class BudgetedEmbedder(ITextEmbedder inner, LlmCallBudget budget) : ITextEmbedder
{
    public string ModelName => inner.ModelName;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        await budget.ReserveOrThrowAsync(cancellationToken);
        return await inner.EmbedAsync(text, cancellationToken);
    }
}
