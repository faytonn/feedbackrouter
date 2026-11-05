using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FeedbackRouter.Api;
using FeedbackRouter.Api.Feedback;
using FeedbackRouter.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FeedbackRouter.Tests.Endpoints;

public sealed class ReviewEndpointTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiFactory _factory = new(requireSignIn: true);
    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _factory.InitializeDatabaseAsync();
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, string? reviewer, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (reviewer is not null)
        {
            request.Headers.Add(ReviewerIdentity.PrincipalNameHeader, reviewer);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        return _client.SendAsync(request, Ct);
    }

    private Task<(FeedbackStatus Status, string? Reviewer)> StateOf(int id) =>
        _factory.WithDbAsync(db => db.FeedbackItems.Where(i => i.Id == id)
            .Select(i => new ValueTuple<FeedbackStatus, string?>(i.Status, i.ReviewerName)).SingleAsync(Ct));

    private Task<int> AddNewItemAsync() => _factory.WithDbAsync(async db =>
    {
        var item = new FeedbackItem
        {
            RawText = "Solution Explorer collapses after every build",
            Source = FeedbackSource.Form,
            SubmittedAt = DateTime.UtcNow.AddHours(-1),
        };
        db.FeedbackItems.Add(item);
        await db.SaveChangesAsync(Ct);
        return item.Id;
    });

    [Fact]
    public async Task Reviewer_endpoints_require_sign_in_but_submit_does_not()
    {
        var queue = await Send(HttpMethod.Get, "/api/queue", reviewer: null);
        var submit = await Send(HttpMethod.Post, "/api/feedback", reviewer: null,
            new { text = "The build output window freezes on long logs." });
        var me = await _client.GetFromJsonAsync<MeDto>("/api/me", Json, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, queue.StatusCode);
        Assert.Equal(HttpStatusCode.Created, submit.StatusCode);
        Assert.Null(me!.Name);
        Assert.StartsWith("/.auth/login/aad", me.SignInUrl);
    }

    [Fact]
    public async Task Decisions_are_recorded_under_the_signed_in_name()
    {
        var id = await _factory.AddProposedItemAsync();

        var response = await Send(HttpMethod.Post, $"/api/feedback/{id}/decision", "ana@contoso.com", new { action = "Dismiss" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var decision = await _factory.WithDbAsync(db => db.TriageDecisions.SingleAsync(d => d.FeedbackItemId == id, Ct));
        Assert.Equal("ana@contoso.com", decision.ReviewerName);
    }

    [Fact]
    public async Task Start_review_claims_the_item_for_one_reviewer()
    {
        var id = await _factory.AddProposedItemAsync();

        var start = await Send(HttpMethod.Post, $"/api/feedback/{id}/review", "ana");
        var other = await Send(HttpMethod.Post, $"/api/feedback/{id}/review", "ben");
        var again = await Send(HttpMethod.Post, $"/api/feedback/{id}/review", "ana");

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var detail = await start.Content.ReadFromJsonAsync<FeedbackDetailDto>(Json, Ct);
        Assert.Equal(FeedbackStatus.InReview, detail!.Status);
        Assert.Equal("ana", detail.Review!.ReviewerName);
        Assert.Equal(ReviewService.IdleTimeout, detail.Review.ExpiresAt - detail.Review.LastActivityAt);

        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        Assert.Contains("Being reviewed by ana", await other.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task Another_reviewer_cannot_decide_a_claimed_item()
    {
        var id = await _factory.AddProposedItemAsync();
        await Send(HttpMethod.Post, $"/api/feedback/{id}/review", "ana");

        var approve = await Send(HttpMethod.Post, $"/api/feedback/{id}/decision", "ben", new { action = "Approve" });
        var dismiss = await Send(HttpMethod.Post, $"/api/feedback/{id}/decision", "ben", new { action = "Dismiss" });

        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, dismiss.StatusCode);
        Assert.Empty(_factory.Filer.Requests);
        Assert.Equal((FeedbackStatus.InReview, "ana"), await StateOf(id));
    }

    [Fact]
    public async Task The_claim_holder_can_approve_and_the_claim_is_cleared()
    {
        var id = await _factory.AddProposedItemAsync();
        await Send(HttpMethod.Post, $"/api/feedback/{id}/review", "ana");

        var approve = await Send(HttpMethod.Post, $"/api/feedback/{id}/decision", "ana", new { action = "Approve" });

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal((FeedbackStatus.Filed, (string?)null), await StateOf(id));
    }

    [Fact]
    public async Task Release_returns_the_item_to_its_previous_status()
    {
        var proposed = await _factory.AddProposedItemAsync();
        var untriaged = await AddNewItemAsync();
        await Send(HttpMethod.Post, $"/api/feedback/{proposed}/review", "ana");
        await Send(HttpMethod.Post, $"/api/feedback/{untriaged}/review", "ana");

        var byOther = await Send(HttpMethod.Delete, $"/api/feedback/{proposed}/review", "ben");
        var first = await Send(HttpMethod.Delete, $"/api/feedback/{proposed}/review", "ana");
        var second = await Send(HttpMethod.Delete, $"/api/feedback/{untriaged}/review", "ana");

        Assert.Equal(HttpStatusCode.Conflict, byOther.StatusCode);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((FeedbackStatus.Proposed, (string?)null), await StateOf(proposed));
        Assert.Equal((FeedbackStatus.New, (string?)null), await StateOf(untriaged));
    }

    [Fact]
    public async Task Idle_claims_are_released_and_can_be_taken_over()
    {
        var idle = await _factory.AddProposedItemAsync();
        var active = await _factory.AddProposedItemAsync("Second item text");
        await Send(HttpMethod.Post, $"/api/feedback/{idle}/review", "ana");
        await Send(HttpMethod.Post, $"/api/feedback/{active}/review", "ana");
        await _factory.WithDbAsync(db => db.FeedbackItems.Where(i => i.Id == idle)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ReviewActivityAt, DateTime.UtcNow.AddMinutes(-31)), Ct));

        var takeover = await Send(HttpMethod.Post, $"/api/feedback/{idle}/review", "ben");
        Assert.Equal(HttpStatusCode.OK, takeover.StatusCode);
        await _factory.WithDbAsync(db => db.FeedbackItems.Where(i => i.Id == idle)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ReviewActivityAt, DateTime.UtcNow.AddMinutes(-31)), Ct));

        await using var scope = _factory.Services.CreateAsyncScope();
        var released = await scope.ServiceProvider.GetRequiredService<ReviewService>().ReleaseIdleAsync(Ct);

        Assert.Equal(1, released);
        Assert.Equal((FeedbackStatus.Proposed, (string?)null), await StateOf(idle));
        Assert.Equal((FeedbackStatus.InReview, "ana"), await StateOf(active));
    }

    [Fact]
    public async Task An_untriaged_item_can_be_approved_with_every_field_set()
    {
        var id = await AddNewItemAsync();

        var partial = await Send(HttpMethod.Post, $"/api/feedback/{id}/decision", "ana",
            new { action = "Approve", title = "Solution Explorer collapses after build" });
        var full = await Send(HttpMethod.Post, $"/api/feedback/{id}/decision", "ana", new
        {
            action = "Approve",
            area = "Editor",
            type = "Bug",
            severity = 3,
            title = "Solution Explorer collapses after build",
            summary = "Solution Explorer collapses all nodes after each build. Reported on 17.12.",
        });

        Assert.Equal(HttpStatusCode.BadRequest, partial.StatusCode);
        Assert.Contains("Set area, type, severity, title and summary", await partial.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        Assert.Equal(Area.Editor, Assert.Single(_factory.Filer.Requests).Area);
    }
}
