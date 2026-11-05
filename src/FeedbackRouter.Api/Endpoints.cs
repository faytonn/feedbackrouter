using System.Text.Json;
using FeedbackRouter.Api.Feedback;
using FeedbackRouter.Api.Triage;
using FeedbackRouter.Core.Domain;
using FeedbackRouter.Infra.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FeedbackRouter.Api;

public static class Endpoints
{
    public const string SubmitRateLimitPolicy = "submit";
    public const int MinTextLength = 10;
    public const int MaxTextLength = 10_000;

    public static void MapFeedbackRouter(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // Public: anyone can submit feedback.
        api.MapPost("/feedback", SubmitAsync).RequireRateLimiting(SubmitRateLimitPolicy);
        api.MapGet("/me", (ReviewerIdentity reviewer) => new MeDto(reviewer.Name, reviewer.RequireSignIn, SignInUrl, SignOutUrl));

        // Reviewers only.
        var review = api.MapGroup("").AddEndpointFilter(RequireReviewerAsync);
        review.MapGet("/queue", QueueAsync);
        review.MapGet("/feedback/{id:int}", DetailAsync);
        review.MapPost("/feedback/{id:int}/review", StartReviewAsync);
        review.MapDelete("/feedback/{id:int}/review", ReleaseReviewAsync);
        review.MapPost("/feedback/{id:int}/decision", DecideAsync);
        review.MapGet("/stats", (StatsQuery stats, CancellationToken ct) => stats.GetAsync(ct));

        if (app.Environment.IsDevelopment())
        {
            review.MapPost("/seed", SeedAsync);
        }

        api.MapFallback(() => Results.NotFound());
        app.MapHealthChecks("/healthz");
    }

    // Container Apps built-in sign-in with the Microsoft provider.
    private const string SignInUrl = "/.auth/login/aad?post_login_redirect_uri=/";
    private const string SignOutUrl = "/.auth/logout?post_logout_redirect_uri=/";

    private static async ValueTask<object?> RequireReviewerAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var reviewer = context.HttpContext.RequestServices.GetRequiredService<ReviewerIdentity>();
        return reviewer.Name is null
            ? TypedResults.Problem("Sign in to review feedback.", statusCode: StatusCodes.Status401Unauthorized)
            : await next(context);
    }

    private static async Task<Results<Created<SubmitFeedbackResponse>, ValidationProblem>> SubmitAsync(
        SubmitFeedbackRequest request, FeedbackDbContext db, TriageSignal signal, TimeProvider clock, CancellationToken ct)
    {
        var text = request.Text?.Trim() ?? string.Empty;
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        var errors = new Dictionary<string, string[]>();

        if (text.Length is < MinTextLength or > MaxTextLength)
        {
            errors["text"] = [$"Feedback must be {MinTextLength} to {MaxTextLength} characters."];
        }

        if (email is not null && (email.Length > 320 || !email.Contains('@') || email.Contains(' ')))
        {
            errors["email"] = ["That does not look like an email address."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var item = new FeedbackItem
        {
            RawText = text,
            SubmitterEmail = email,
            Source = string.Equals(request.Source, "form", StringComparison.OrdinalIgnoreCase)
                ? FeedbackSource.Form
                : FeedbackSource.Api,
            SubmittedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.FeedbackItems.Add(item);
        await db.SaveChangesAsync(ct);
        signal.Notify();

        return TypedResults.Created($"/api/feedback/{item.Id}", new SubmitFeedbackResponse(item.Id, item.Status));
    }

    private static Task<IReadOnlyList<QueueItemDto>> QueueAsync(
        FeedbackStatus? status, Area? area, FeedbackQueries queries, CancellationToken ct) =>
        queries.QueueAsync(status, area, ct);

    private static async Task<Results<Ok<FeedbackDetailDto>, NotFound>> DetailAsync(
        int id, FeedbackQueries queries, CancellationToken ct) =>
        await queries.DetailAsync(id, ct) is { } detail ? TypedResults.Ok(detail) : TypedResults.NotFound();

    private static async Task<IResult> DecideAsync(
        int id, DecisionRequest request, DecisionService decisions, FeedbackQueries queries, ReviewerIdentity reviewer,
        CancellationToken ct)
    {
        var outcome = await decisions.DecideAsync(id, request, reviewer.Name!, ct);
        return outcome switch
        {
            DecisionOutcome.Done => TypedResults.Ok(await queries.DetailAsync(id, ct)),
            DecisionOutcome.NotFound => TypedResults.NotFound(),
            DecisionOutcome.Invalid i => TypedResults.Problem(i.Message, statusCode: StatusCodes.Status400BadRequest),
            DecisionOutcome.Conflict c => TypedResults.Problem(c.Message, statusCode: StatusCodes.Status409Conflict),
            DecisionOutcome.FilingFailed f => TypedResults.Problem(
                f.Message, title: "Azure DevOps filing failed", statusCode: StatusCodes.Status502BadGateway),
            _ => throw new InvalidOperationException($"Unhandled outcome {outcome}"),
        };
    }

    private static async Task<IResult> StartReviewAsync(
        int id, ReviewService reviews, FeedbackQueries queries, ReviewerIdentity reviewer, CancellationToken ct) =>
        ToResult(await reviews.StartAsync(id, reviewer.Name!, ct), await queries.DetailAsync(id, ct));

    private static async Task<IResult> ReleaseReviewAsync(
        int id, ReviewService reviews, FeedbackQueries queries, ReviewerIdentity reviewer, CancellationToken ct) =>
        ToResult(await reviews.ReleaseAsync(id, reviewer.Name!, ct), await queries.DetailAsync(id, ct));

    private static IResult ToResult(ReviewOutcome outcome, FeedbackDetailDto? detail) => outcome switch
    {
        ReviewOutcome.Done => TypedResults.Ok(detail),
        ReviewOutcome.NotFound => TypedResults.NotFound(),
        ReviewOutcome.Conflict c => TypedResults.Problem(c.Message, statusCode: StatusCodes.Status409Conflict),
        _ => throw new InvalidOperationException($"Unhandled outcome {outcome}"),
    };

    private sealed record SeedItem(string Text, string? Email, double AgeHours);

    private static async Task<IResult> SeedAsync(
        FeedbackDbContext db, TriageSignal signal, TimeProvider clock, IConfiguration config, IHostEnvironment env,
        CancellationToken ct)
    {
        if (await db.FeedbackItems.AnyAsync(i => i.Source == FeedbackSource.Import, ct))
        {
            return TypedResults.Problem("Seed items are already imported.", statusCode: StatusCodes.Status409Conflict);
        }

        var path = Path.GetFullPath(config["Seed:Path"] ?? "../../seed/feedback.json", env.ContentRootPath);
        if (!File.Exists(path))
        {
            return TypedResults.Problem($"Seed file not found at {path}.", statusCode: StatusCodes.Status500InternalServerError);
        }

        await using var stream = File.OpenRead(path);
        var seed = await JsonSerializer.DeserializeAsync<List<SeedItem>>(stream, JsonSerializerOptions.Web, ct) ?? [];
        var now = clock.GetUtcNow().UtcDateTime;

        db.FeedbackItems.AddRange(seed.Select(s => new FeedbackItem
        {
            RawText = s.Text.Trim(),
            SubmitterEmail = s.Email,
            Source = FeedbackSource.Import,
            SubmittedAt = now.AddHours(-s.AgeHours),
        }));
        await db.SaveChangesAsync(ct);
        signal.Notify();

        return TypedResults.Ok(new { imported = seed.Count });
    }
}
