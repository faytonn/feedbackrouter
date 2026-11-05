using System.Net;
using System.Net.Http.Json;
using FeedbackRouter.Api;
using FeedbackRouter.Api.Triage;
using FeedbackRouter.Core.Domain;
using FeedbackRouter.Core.Triage;
using FeedbackRouter.Tests.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FeedbackRouter.Tests.Triage;

public sealed class LlmCallBudgetTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 11, 5, 22, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _factory.InitializeDatabaseAsync();

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private LlmCallBudget Budget(int cap) => new(
        _factory.Services.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new TriageOptions { DailyLlmCallCap = cap }),
        _clock);

    [Fact]
    public async Task Calls_stop_at_the_cap_and_start_again_the_next_utc_day()
    {
        var budget = Budget(cap: 3);

        var today = new List<bool>();
        for (var i = 0; i < 4; i++)
        {
            today.Add(await budget.TryReserveAsync(Ct));
        }

        _clock.Now = _clock.Now.AddHours(3);
        var tomorrow = await budget.TryReserveAsync(Ct);

        Assert.Equal([true, true, true, false], today);
        Assert.True(tomorrow);
        Assert.Equal(1, await budget.UsedTodayAsync(Ct));
    }

    [Fact]
    public async Task A_cap_of_zero_allows_no_calls()
    {
        Assert.False(await Budget(cap: 0).TryReserveAsync(Ct));
        Assert.False(await Budget(cap: 0).HasRemainingAsync(Ct));
    }

    [Fact]
    public async Task Over_the_cap_the_model_is_not_called()
    {
        var budget = Budget(cap: 1);
        var inner = new FakeTriageModel("first");
        var model = new BudgetedTriageModel(inner, budget);
        var embedder = new BudgetedEmbedder(new FakeEmbedder([1f, 0f]), budget);

        var reply = await model.CompleteAsync([], Ct);
        await Assert.ThrowsAsync<LlmBudgetExhaustedException>(() => model.CompleteAsync([], Ct));
        await Assert.ThrowsAsync<LlmBudgetExhaustedException>(() => embedder.EmbedAsync("text", Ct));

        Assert.Equal("first", reply);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Seed_items_arrive_with_their_stored_proposals()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SeedImporter>().ImportAsync(Ct);

        var items = await _factory.WithDbAsync(db => db.FeedbackItems.Include(i => i.Proposals).ToListAsync(Ct));

        Assert.Equal(40, items.Count);
        Assert.All(items, i =>
        {
            Assert.Equal(FeedbackStatus.Proposed, i.Status);
            var proposal = Assert.Single(i.Proposals);
            Assert.Equal("triage.v1", proposal.PromptVersion);
            Assert.Contains("pre-generated", proposal.ModelName);
        });
    }

    [Fact]
    public async Task Feedback_longer_than_4000_characters_is_rejected()
    {
        var client = _factory.CreateClient();

        var tooLong = await client.PostAsJsonAsync("/api/feedback", new { text = new string('a', 4_001) }, Ct);
        var atLimit = await client.PostAsJsonAsync("/api/feedback", new { text = new string('a', 4_000) }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.Created, atLimit.StatusCode);
        Assert.Equal(4_000, FeedbackRouter.Api.Endpoints.MaxTextLength);
    }
}
