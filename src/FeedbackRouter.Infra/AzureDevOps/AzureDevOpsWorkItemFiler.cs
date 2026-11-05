using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FeedbackRouter.Core;
using FeedbackRouter.Core.Domain;
using FeedbackRouter.Core.Filing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FeedbackRouter.Infra.AzureDevOps;

/// <summary>
/// Creates work items through the Azure DevOps REST API 7.1 (JSON Patch).
/// Only 429 is retried: creating a work item is not idempotent, and a 429 is
/// the one failure where Azure DevOps guarantees nothing was created.
/// </summary>
public sealed partial class AzureDevOpsWorkItemFiler(
    HttpClient http,
    IOptions<AzureDevOpsOptions> options,
    ILogger<AzureDevOpsWorkItemFiler> logger,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IWorkItemFiler
{
    public const string ApiVersion = "7.1";

    private readonly AzureDevOpsOptions _options = options.Value;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<FilingResult> FileAsync(WorkItemRequest request, CancellationToken cancellationToken)
    {
        var workItemType = _options.WorkItemTypes[request.Type];
        var url = BuildUrl(workItemType);
        var patch = BuildPatch(request, workItemType);

        using var activity = Telemetry.Source.StartActivity("ado.create_work_item");
        activity?.SetTag("feedback.id", request.FeedbackItemId);
        activity?.SetTag("ado.project", _options.Project);
        activity?.SetTag("ado.work_item_type", workItemType);
        activity?.SetTag("ado.dry_run", _options.DryRun);

        if (_options.DryRun)
        {
            LogDryRun(logger, request.FeedbackItemId, url, patch.ToJsonString());
            return new FilingResult(null, null, DryRun: true, _options.Project);
        }

        if (string.IsNullOrWhiteSpace(_options.Pat))
        {
            throw new WorkItemFilingException("Azure DevOps PAT is not configured. Set AzureDevOps:Pat or turn on DryRun.");
        }

        for (var attempt = 0; ; attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(patch.ToJsonString(), Encoding.UTF8, "application/json-patch+json"),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + _options.Pat)));

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(message, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new WorkItemFilingException($"Could not reach Azure DevOps: {ex.Message}", null, ex);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    var result = await ReadResultAsync(response, cancellationToken);
                    activity?.SetTag("ado.work_item_id", result.WorkItemId);
                    return result;
                }

                var status = (int)response.StatusCode;
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < _options.MaxRetries)
                {
                    var wait = RetryDelay(response, attempt);
                    LogThrottled(logger, request.FeedbackItemId, attempt + 1, wait.TotalSeconds);
                    await _delay(wait, cancellationToken);
                    continue;
                }

                var detail = await ReadErrorAsync(response, cancellationToken);
                throw response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new WorkItemFilingException(
                        $"Azure DevOps rejected the PAT ({status}). Check that it is valid and has Work Items read and write scope.",
                        status),
                    HttpStatusCode.TooManyRequests => new WorkItemFilingException(
                        $"Azure DevOps is throttling requests (429) and {_options.MaxRetries} retries did not get through.",
                        status),
                    _ => new WorkItemFilingException($"Azure DevOps returned {status}: {detail}", status),
                };
            }
        }
    }

    private Uri BuildUrl(string workItemType) => new(
        $"{_options.BaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(_options.Organization)}/{Uri.EscapeDataString(_options.Project)}" +
        $"/_apis/wit/workitems/${Uri.EscapeDataString(workItemType)}?api-version={ApiVersion}");

    private JsonArray BuildPatch(WorkItemRequest request, string workItemType)
    {
        var body =
            $"<p>{WebUtility.HtmlEncode(request.Summary)}</p>" +
            $"<p>Original feedback (FeedbackRouter item {request.FeedbackItemId}):</p>" +
            $"<pre>{WebUtility.HtmlEncode(request.RawText)}</pre>";

        // Bugs show Repro Steps on their form instead of Description.
        var bodyField = workItemType == "Bug" ? "Microsoft.VSTS.TCM.ReproSteps" : "System.Description";

        var patch = new JsonArray
        {
            Add("/fields/System.Title", request.Title),
            Add($"/fields/{bodyField}", body),
            Add("/fields/System.Tags", $"feedbackrouter; area:{Labels.Of(request.Area)}; feedback:{request.FeedbackItemId}"),
        };

        if (workItemType == "Bug")
        {
            patch.Add(Add("/fields/Microsoft.VSTS.Common.Severity", SeverityValue(request.Severity)));
        }

        if (_options.AreaPaths.TryGetValue(request.Area, out var areaPath))
        {
            patch.Add(Add("/fields/System.AreaPath", areaPath));
        }

        return patch;
    }

    private static JsonObject Add(string path, string value) => new()
    {
        ["op"] = "add",
        ["path"] = path,
        ["value"] = value,
    };

    private static string SeverityValue(int severity) => severity switch
    {
        1 => "1 - Critical",
        2 => "2 - High",
        3 => "3 - Medium",
        _ => "4 - Low",
    };

    private async Task<FilingResult> ReadResultAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        var id = root.GetProperty("id").GetInt32();

        var url = root.TryGetProperty("_links", out var links)
                  && links.TryGetProperty("html", out var html)
                  && html.TryGetProperty("href", out var href)
            ? href.GetString()
            : null;
        url ??= $"{_options.BaseUrl.TrimEnd('/')}/{_options.Organization}/{_options.Project}/_workitems/edit/{id}";

        return new FilingResult(id, url, DryRun: false, _options.Project);
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("message", out var m) && m.GetString() is { } msg)
            {
                return msg;
            }
        }
        catch (JsonException)
        {
            // Not JSON; fall through to the raw text.
        }

        return text.Length > 300 ? text[..300] : text;
    }

    private TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        var wait = retryAfter?.Delta
                   ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null)
                   ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
        if (wait < TimeSpan.Zero)
        {
            wait = TimeSpan.Zero;
        }

        return wait > _options.MaxRetryDelay ? _options.MaxRetryDelay : wait;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Dry run: would create work item for feedback {FeedbackId}: POST {Url} {Body}")]
    private static partial void LogDryRun(ILogger logger, int feedbackId, Uri url, string body);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Azure DevOps throttled filing of feedback {FeedbackId}; retry {Attempt} in {DelaySeconds}s")]
    private static partial void LogThrottled(ILogger logger, int feedbackId, int attempt, double delaySeconds);
}
