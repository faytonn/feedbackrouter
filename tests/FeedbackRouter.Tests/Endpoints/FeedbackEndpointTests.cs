using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FeedbackRouter.Api;
using FeedbackRouter.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace FeedbackRouter.Tests.Endpoints;

public sealed class FeedbackEndpointTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiFactory _factory = new();
    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _factory.InitializeDatabaseAsync();
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Submit_stores_the_item_as_new()
    {
        var response = await _client.PostAsJsonAsync("/api/feedback",
            new { text = "  Solution Explorer collapses every folder after a branch switch.  ", source = "form" }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SubmitFeedbackResponse>(Json, Ct);
        var item = await _factory.WithDbAsync(db => db.FeedbackItems.SingleAsync(i => i.Id == body!.Id, Ct));
        Assert.Equal(FeedbackStatus.New, item.Status);
        Assert.Equal(FeedbackSource.Form, item.Source);
        Assert.Equal("Solution Explorer collapses every folder after a branch switch.", item.RawText);
    }

    [Theory]
    [InlineData("short", null)]
    [InlineData("Long enough feedback text here", "not-an-email")]
    public async Task Submit_rejects_invalid_input(string text, string? email)
    {
        var response = await _client.PostAsJsonAsync("/api/feedback", new { text, email }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Queue_is_sorted_by_severity_then_age_and_filters_by_status()
    {
        var now = DateTime.UtcNow;
        var sev3Old = await _factory.AddProposedItemAsync("sev 3 old", 3, now.AddDays(-3));
        var sev1New = await _factory.AddProposedItemAsync("sev 1 new", 1, now.AddHours(-1));
        var sev1Old = await _factory.AddProposedItemAsync("sev 1 old", 1, now.AddDays(-2));
        var dismissed = await _factory.AddProposedItemAsync("dismissed", 1, now.AddDays(-5), FeedbackStatus.Dismissed);

        var all = await _client.GetFromJsonAsync<List<QueueItemDto>>("/api/queue", Json, Ct);
        var proposed = await _client.GetFromJsonAsync<List<QueueItemDto>>("/api/queue?status=Proposed", Json, Ct);

        Assert.Equal([dismissed, sev1Old, sev1New, sev3Old], all!.Select(i => i.Id));
        Assert.All(all!, i => Assert.Equal(DateTimeKind.Utc, i.SubmittedAt.Kind));
        Assert.Equal([sev1Old, sev1New, sev3Old], proposed!.Select(i => i.Id));
    }

    [Fact]
    public async Task Detail_includes_duplicates_with_similarity()
    {
        var original = await _factory.AddProposedItemAsync("Breakpoints fire twice in async code");
        var dup = await _factory.AddProposedItemAsync("async bp hit 2x");
        await _factory.WithDbAsync(async db =>
        {
            var proposal = await db.TriageProposals.Include(p => p.DuplicateCandidates)
                .SingleAsync(p => p.FeedbackItemId == dup, Ct);
            proposal.DuplicateCandidates.Add(new DuplicateCandidate { FeedbackItemId = original, Similarity = 0.9132 });
            return await db.SaveChangesAsync(Ct);
        });

        var detail = await _client.GetFromJsonAsync<FeedbackDetailDto>($"/api/feedback/{dup}", Json, Ct);

        var candidate = Assert.Single(detail!.Duplicates);
        Assert.Equal(original, candidate.Id);
        Assert.Equal("Breakpoints fire twice in async code", candidate.Title);
        Assert.Equal(0.9132, candidate.Similarity);
        Assert.Equal("triage.v1", detail.Proposal!.PromptVersion);
    }

    [Fact]
    public async Task Stats_counts_areas_and_duplicate_rate()
    {
        var a = await _factory.AddProposedItemAsync("a");
        var b = await _factory.AddProposedItemAsync("b");
        await _client.PostAsJsonAsync($"/api/feedback/{b}/decision", new { action = "Merge", mergeIntoId = a }, Ct);
        await _client.PostAsJsonAsync($"/api/feedback/{a}/decision", new { action = "Approve" }, Ct);

        var stats = await _client.GetFromJsonAsync<StatsDto>("/api/stats", Json, Ct);

        Assert.Equal(2, stats!.ByArea.Single(x => x.Key == "Debugger").Count);
        Assert.Equal(0.5, stats.DuplicateRate);
        Assert.Equal(2, stats.Decided);
        Assert.Equal(1, stats.FiledThisWeek);
        Assert.Equal(0, stats.Open);
        Assert.Equal(30, stats.PerDay.Count);
        Assert.NotNull(stats.MedianTimeToTriageSeconds);
    }

    [Fact]
    public async Task Seed_endpoint_does_not_exist_outside_development()
    {
        var response = await _client.PostAsync("/api/seed", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
