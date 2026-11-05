using FeedbackRouter.Core.Domain;
using FeedbackRouter.Core.Filing;
using FeedbackRouter.Infra.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FeedbackRouter.Tests.Endpoints;

/// <summary>
/// The real API over a SQLite file database. A file rather than :memory: so
/// each request gets its own connection and concurrent requests behave like
/// they would against SQL Server.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"feedbackrouter-{Guid.NewGuid():N}.db");

    public FakeFiler Filer { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Llm:ApiKey", "");
        builder.UseSetting("Telemetry:ConsoleExporter", "false");
        builder.UseSetting("ConnectionStrings:FeedbackRouter", "unused");

        // Migrations target SQL Server; the SQLite schema comes from EnsureCreated.
        builder.UseSetting("Database:MigrateOnStartup", "false");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<FeedbackDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FeedbackDbContext>>();
            services.AddDbContext<FeedbackDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));

            services.RemoveAll<IWorkItemFiler>();
            services.AddSingleton<IWorkItemFiler>(Filer);
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FeedbackDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task<T> WithDbAsync<T>(Func<FeedbackDbContext, Task<T>> work)
    {
        await using var scope = Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<FeedbackDbContext>());
    }

    /// <summary>Adds an item that the pipeline has already proposed.</summary>
    public Task<int> AddProposedItemAsync(
        string text = "Debugger hits breakpoints twice in async methods",
        int severity = 2,
        DateTime? submittedAt = null,
        FeedbackStatus status = FeedbackStatus.Proposed) =>
        WithDbAsync(async db =>
        {
            var item = new FeedbackItem
            {
                RawText = text,
                Source = FeedbackSource.Api,
                SubmittedAt = submittedAt ?? DateTime.UtcNow.AddHours(-3),
                Status = status,
                Proposals =
                [
                    new TriageProposal
                    {
                        Area = Area.Debugger,
                        Type = FeedbackType.Bug,
                        Severity = severity,
                        Title = text,
                        Summary = "Proposed summary. Second sentence.",
                        Confidence = 0.8,
                        ProposedAt = DateTime.UtcNow.AddHours(-2),
                        ModelName = "fake-model",
                        PromptVersion = "triage.v1",
                    },
                ],
            };
            db.FeedbackItems.Add(item);
            await db.SaveChangesAsync();
            return item.Id;
        });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Best effort; the temp folder is cleaned eventually.
        }
    }
}

/// <summary>Records every filing; can be told to fail or to block until released.</summary>
public sealed class FakeFiler : IWorkItemFiler
{
    private int _nextId = 5000;

    public List<WorkItemRequest> Requests { get; } = [];

    public Exception? FailWith { get; set; }

    public TaskCompletionSource? Gate { get; set; }

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<FilingResult> FileAsync(WorkItemRequest request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        Entered.TrySetResult();
        if (Gate is not null)
        {
            await Gate.Task;
        }

        if (FailWith is not null)
        {
            throw FailWith;
        }

        var id = Interlocked.Increment(ref _nextId);
        return new FilingResult(id, $"https://dev.azure.com/contoso/Feedback/_workitems/edit/{id}", DryRun: false, "Feedback");
    }
}
