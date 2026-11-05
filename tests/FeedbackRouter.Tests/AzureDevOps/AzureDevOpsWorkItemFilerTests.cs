using System.Net;
using System.Text.Json;
using FeedbackRouter.Core.Domain;
using FeedbackRouter.Core.Filing;
using FeedbackRouter.Infra.AzureDevOps;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FeedbackRouter.Tests.AzureDevOps;

public class AzureDevOpsWorkItemFilerTests
{
    private static readonly WorkItemRequest Request = new(
        FeedbackItemId: 42,
        Area: Area.Debugger,
        Type: FeedbackType.Bug,
        Severity: 2,
        Title: "Breakpoints in async methods are hit twice",
        Summary: "Breakpoints in async methods are hit twice. Started in 17.12.",
        RawText: "bp hit twice <async> since 17.12");

    private const string CreatedBody = """
        {"id": 1234, "url": "https://dev.azure.com/contoso/_apis/wit/workItems/1234",
         "_links": {"html": {"href": "https://dev.azure.com/contoso/Feedback/_workitems/edit/1234"}}}
        """;

    private static (AzureDevOpsWorkItemFiler Filer, FakeHandler Handler, List<TimeSpan> Delays) Create(
        bool dryRun = false, params Func<HttpResponseMessage>[] responses)
    {
        var handler = new FakeHandler(responses);
        var delays = new List<TimeSpan>();
        var options = Options.Create(new AzureDevOpsOptions
        {
            Organization = "contoso",
            Project = "Feedback",
            Pat = "secret-pat",
            DryRun = dryRun,
        });
        var filer = new AzureDevOpsWorkItemFiler(
            new HttpClient(handler),
            options,
            NullLogger<AzureDevOpsWorkItemFiler>.Instance,
            (d, _) =>
            {
                delays.Add(d);
                return Task.CompletedTask;
            });
        return (filer, handler, delays);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Success_posts_json_patch_and_returns_id_and_html_link()
    {
        var (filer, handler, _) = Create(responses: () => Json(HttpStatusCode.OK, CreatedBody));

        var result = await filer.FileAsync(Request, TestContext.Current.CancellationToken);

        Assert.Equal(1234, result.WorkItemId);
        Assert.Equal("https://dev.azure.com/contoso/Feedback/_workitems/edit/1234", result.Url);
        Assert.False(result.DryRun);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://dev.azure.com/contoso/Feedback/_apis/wit/workitems/$Bug?api-version=7.1", sent.Uri);
        Assert.Equal("application/json-patch+json", sent.ContentType);
        Assert.Equal("Basic " + Convert.ToBase64String(":secret-pat"u8.ToArray()), sent.Authorization);

        using var patch = JsonDocument.Parse(sent.Body);
        var ops = patch.RootElement.EnumerateArray().ToDictionary(
            o => o.GetProperty("path").GetString()!, o => o.GetProperty("value").GetString()!);
        Assert.Equal("Breakpoints in async methods are hit twice", ops["/fields/System.Title"]);
        Assert.Equal("2 - High", ops["/fields/Microsoft.VSTS.Common.Severity"]);
        Assert.Contains("&lt;async&gt;", ops["/fields/Microsoft.VSTS.TCM.ReproSteps"]);
        Assert.Contains("feedback:42", ops["/fields/System.Tags"]);
    }

    [Fact]
    public async Task Feature_request_uses_user_story_and_description_without_severity()
    {
        var (filer, handler, _) = Create(responses: () => Json(HttpStatusCode.OK, CreatedBody));

        await filer.FileAsync(Request with { Type = FeedbackType.FeatureRequest }, TestContext.Current.CancellationToken);

        var sent = Assert.Single(handler.Requests);
        Assert.Contains("/_apis/wit/workitems/$User%20Story?", sent.Uri);
        Assert.Contains("System.Description", sent.Body);
        Assert.DoesNotContain("Severity", sent.Body);
    }

    [Fact]
    public async Task Unauthorized_throws_without_retrying()
    {
        var (filer, handler, delays) = Create(responses: () => Json(HttpStatusCode.Unauthorized, "{}"));

        var ex = await Assert.ThrowsAsync<WorkItemFilingException>(
            () => filer.FileAsync(Request, TestContext.Current.CancellationToken));

        Assert.Equal(401, ex.StatusCode);
        Assert.Contains("rejected the PAT", ex.Message);
        Assert.Single(handler.Requests);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Too_many_requests_waits_for_retry_after_then_succeeds()
    {
        var (filer, handler, delays) = Create(responses:
        [
            () =>
            {
                var r = Json(HttpStatusCode.TooManyRequests, "{}");
                r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
                return r;
            },
            () => Json(HttpStatusCode.TooManyRequests, "{}"),
            () => Json(HttpStatusCode.OK, CreatedBody),
        ]);

        var result = await filer.FileAsync(Request, TestContext.Current.CancellationToken);

        Assert.Equal(1234, result.WorkItemId);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(2)], delays);
    }

    [Fact]
    public async Task Too_many_requests_gives_up_after_max_retries()
    {
        var (filer, handler, _) = Create(responses: Enumerable.Repeat(
            () => Json(HttpStatusCode.TooManyRequests, "{}"), 10).ToArray());

        var ex = await Assert.ThrowsAsync<WorkItemFilingException>(
            () => filer.FileAsync(Request, TestContext.Current.CancellationToken));

        Assert.Equal(429, ex.StatusCode);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task Bad_request_surfaces_the_azure_devops_message()
    {
        var (filer, _, _) = Create(responses: () => Json(HttpStatusCode.BadRequest,
            """{"message": "TF401320: Rule Error for field Area Path."}"""));

        var ex = await Assert.ThrowsAsync<WorkItemFilingException>(
            () => filer.FileAsync(Request, TestContext.Current.CancellationToken));

        Assert.Equal("Azure DevOps returned 400: TF401320: Rule Error for field Area Path.", ex.Message);
    }

    [Fact]
    public async Task Dry_run_sends_nothing()
    {
        var (filer, handler, _) = Create(dryRun: true);

        var result = await filer.FileAsync(Request, TestContext.Current.CancellationToken);

        Assert.True(result.DryRun);
        Assert.Null(result.WorkItemId);
        Assert.Equal("Feedback", result.Project);
        Assert.Empty(handler.Requests);
    }

    private sealed record SentRequest(HttpMethod Method, string Uri, string? ContentType, string? Authorization, string Body);

    private sealed class FakeHandler(Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _next;

        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(new SentRequest(
                request.Method,
                request.RequestUri!.AbsoluteUri,
                request.Content?.Headers.ContentType?.MediaType,
                request.Headers.Authorization?.ToString(),
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct)));
            return responses[_next++]();
        }
    }
}
