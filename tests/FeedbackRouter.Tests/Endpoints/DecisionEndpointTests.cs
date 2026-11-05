using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FeedbackRouter.Api;
using FeedbackRouter.Core.Domain;
using FeedbackRouter.Core.Filing;
using Microsoft.EntityFrameworkCore;

namespace FeedbackRouter.Tests.Endpoints;

public sealed class DecisionEndpointTests : IAsyncLifetime
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

    private Task<HttpResponseMessage> Decide(int id, object body) =>
        _client.PostAsJsonAsync($"/api/feedback/{id}/decision", body, Json, Ct);

    private Task<FeedbackStatus> StatusOf(int id) =>
        _factory.WithDbAsync(db => db.FeedbackItems.Where(i => i.Id == id).Select(i => i.Status).SingleAsync(Ct));

    [Fact]
    public async Task Approve_files_once_and_a_second_approve_returns_409()
    {
        var id = await _factory.AddProposedItemAsync();

        var first = await Decide(id, new { action = "Approve" });
        var second = await Decide(id, new { action = "Approve" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var detail = await first.Content.ReadFromJsonAsync<FeedbackDetailDto>(Json, Ct);
        Assert.Equal(FeedbackStatus.Filed, detail!.Status);
        Assert.Equal(5001, detail.WorkItem!.Id);
        Assert.Equal("https://dev.azure.com/contoso/Feedback/_workitems/edit/5001", detail.WorkItem.Url);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("Already filed as work item 5001", await second.Content.ReadAsStringAsync(Ct));
        Assert.Single(_factory.Filer.Requests);
    }

    [Fact]
    public async Task Concurrent_approves_file_exactly_once()
    {
        var id = await _factory.AddProposedItemAsync();
        _factory.Filer.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Decide(id, new { action = "Approve" });
        await _factory.Filer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var second = await Decide(id, new { action = "Approve" });
        _factory.Filer.Gate.SetResult();

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("already in progress", await second.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, (await first).StatusCode);
        Assert.Single(_factory.Filer.Requests);
        Assert.Equal(FeedbackStatus.Filed, await StatusOf(id));
    }

    [Fact]
    public async Task Failed_filing_leaves_item_approved_and_a_retry_files_it()
    {
        var id = await _factory.AddProposedItemAsync();
        _factory.Filer.FailWith = new WorkItemFilingException("Azure DevOps rejected the PAT (401).", 401);

        var failed = await Decide(id, new { action = "Approve" });

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Contains("rejected the PAT", await failed.Content.ReadAsStringAsync(Ct));
        Assert.Equal(FeedbackStatus.Approved, await StatusOf(id));
        Assert.False(await _factory.WithDbAsync(db => db.WorkItemLinks.AnyAsync(l => l.FeedbackItemId == id, Ct)));

        _factory.Filer.FailWith = null;
        var retried = await Decide(id, new { action = "Approve" });

        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal(FeedbackStatus.Filed, await StatusOf(id));
        Assert.Equal(2, _factory.Filer.Requests.Count);
    }

    [Fact]
    public async Task Approve_files_the_reviewer_edits_not_the_proposal()
    {
        var id = await _factory.AddProposedItemAsync();

        var response = await Decide(id, new
        {
            action = "Approve",
            area = "Build",
            type = "FeatureRequest",
            severity = 3,
            title = "Edited title",
            summary = "Edited summary. Still two sentences.",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var filed = Assert.Single(_factory.Filer.Requests);
        Assert.Equal(new WorkItemRequest(id, Area.Build, FeedbackType.FeatureRequest, 3, "Edited title",
            "Edited summary. Still two sentences.", "Debugger hits breakpoints twice in async methods"), filed);

        var decision = await _factory.WithDbAsync(db => db.TriageDecisions.SingleAsync(d => d.FeedbackItemId == id, Ct));
        Assert.Equal(DecisionAction.Approve, decision.Action);
        Assert.Equal("Edited title", decision.FinalTitle);
        Assert.Equal("reviewer", decision.ReviewerName);
    }

    [Fact]
    public async Task Invalid_edits_are_rejected_before_anything_changes()
    {
        var id = await _factory.AddProposedItemAsync();

        var response = await Decide(id, new { action = "Approve", severity = 7 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(FeedbackStatus.Proposed, await StatusOf(id));
        Assert.Empty(_factory.Filer.Requests);
    }

    [Fact]
    public async Task Merge_points_the_item_at_its_original()
    {
        var original = await _factory.AddProposedItemAsync("Breakpoints fire twice in async code");
        var duplicate = await _factory.AddProposedItemAsync("async breakpoints hit 2x");

        var response = await Decide(duplicate, new { action = "Merge", mergeIntoId = original });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<FeedbackDetailDto>(Json, Ct);
        Assert.Equal(FeedbackStatus.Merged, detail!.Status);
        Assert.Equal(original, detail.MergedIntoId);
        Assert.Empty(_factory.Filer.Requests);
    }

    [Fact]
    public async Task Merge_into_a_merged_item_is_rejected()
    {
        var a = await _factory.AddProposedItemAsync("a");
        var b = await _factory.AddProposedItemAsync("b");
        var c = await _factory.AddProposedItemAsync("c");
        await Decide(b, new { action = "Merge", mergeIntoId = a });

        var response = await Decide(c, new { action = "Merge", mergeIntoId = b });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"merged into {a}", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Dismissed_item_cannot_be_approved()
    {
        var id = await _factory.AddProposedItemAsync();
        Assert.Equal(HttpStatusCode.OK, (await Decide(id, new { action = "Dismiss", notes = "spam" })).StatusCode);

        var response = await Decide(id, new { action = "Approve" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(_factory.Filer.Requests);
    }

    [Fact]
    public async Task Unknown_item_returns_404()
    {
        var response = await Decide(999, new { action = "Dismiss" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
