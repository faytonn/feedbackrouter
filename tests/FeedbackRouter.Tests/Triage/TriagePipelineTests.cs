using FeedbackRouter.Core.Domain;
using FeedbackRouter.Core.Triage;

namespace FeedbackRouter.Tests.Triage;

public class TriagePipelineTests
{
    private const string ValidReply = """
        {
          "area": "Debugger",
          "type": "Bug",
          "severity": 2,
          "title": "Breakpoints in async methods are hit twice",
          "summary": "After updating to 17.12 breakpoints inside async methods are hit twice. Stepping past the second hit works.",
          "confidence": 0.82
        }
        """;

    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    private static FeedbackItem Item(int id = 1) => new()
    {
        Id = id,
        RawText = "breakpoints in async methods hit twice since 17.12",
        SubmittedAt = Now.UtcDateTime,
    };

    private static TriagePipeline Pipeline(FakeTriageModel model, FakeEmbedder? embedder = null) =>
        new(embedder ?? new FakeEmbedder([1f, 0f, 0f]), model, TriagePrompt.Load(), new FixedClock(Now));

    [Fact]
    public async Task Valid_reply_produces_proposal_with_prompt_version_and_model()
    {
        var model = new FakeTriageModel(ValidReply);

        var outcome = await Pipeline(model).RunAsync(Item(), [], TestContext.Current.CancellationToken);

        var proposed = Assert.IsType<TriageOutcome.Proposed>(outcome);
        Assert.Equal(Area.Debugger, proposed.Proposal.Area);
        Assert.Equal(FeedbackType.Bug, proposed.Proposal.Type);
        Assert.Equal(2, proposed.Proposal.Severity);
        Assert.Equal("Breakpoints in async methods are hit twice", proposed.Proposal.Title);
        Assert.Equal("triage.v1", proposed.Proposal.PromptVersion);
        Assert.Equal("fake-model", proposed.Proposal.ModelName);
        Assert.Equal(Now.UtcDateTime, proposed.Proposal.ProposedAt);
        Assert.Equal(1, model.Calls);
    }

    [Fact]
    public async Task Invalid_then_valid_retries_once_and_tells_the_model_what_was_wrong()
    {
        var model = new FakeTriageModel("Sure! Here is the triage: area is Debugger.", ValidReply);

        var outcome = await Pipeline(model).RunAsync(Item(), [], TestContext.Current.CancellationToken);

        Assert.IsType<TriageOutcome.Proposed>(outcome);
        Assert.Equal(2, model.Calls);

        var retry = model.Conversations[1];
        Assert.Equal(4, retry.Count);
        Assert.Equal(ChatRole.Assistant, retry[2].Role);
        Assert.Equal(ChatRole.User, retry[3].Role);
        Assert.StartsWith("That reply was not valid: not JSON", retry[3].Text);
    }

    [Fact]
    public async Task Always_invalid_gives_up_after_two_calls_and_reports_the_error()
    {
        var model = new FakeTriageModel(
            """{"area":"Kitchen","type":"Bug","severity":2,"title":"t","summary":"s","confidence":0.5}""",
            """{"area":"Editor","type":"Bug","severity":9,"title":"t","summary":"s","confidence":0.5}""",
            ValidReply);

        var outcome = await Pipeline(model).RunAsync(Item(), [], TestContext.Current.CancellationToken);

        var invalid = Assert.IsType<TriageOutcome.Invalid>(outcome);
        Assert.Equal(2, model.Calls);
        Assert.Contains("severity", invalid.Error);
        Assert.NotNull(invalid.Embedding);
    }

    [Fact]
    public async Task Duplicate_candidates_are_attached_and_their_titles_sent_to_the_model()
    {
        var model = new FakeTriageModel(ValidReply);
        var pool = new[]
        {
            new PoolEntry(7, "Async breakpoints trigger twice", [0.99f, 0.1f, 0f]),
            new PoolEntry(8, "Git Changes window is slow", [0f, 1f, 0f]),
        };

        var outcome = await Pipeline(model).RunAsync(Item(), pool, TestContext.Current.CancellationToken);

        var proposed = Assert.IsType<TriageOutcome.Proposed>(outcome);
        var candidate = Assert.Single(proposed.Proposal.DuplicateCandidates);
        Assert.Equal(7, candidate.FeedbackItemId);
        Assert.Contains("- Async breakpoints trigger twice", model.Conversations[0][1].Text);
        Assert.DoesNotContain("Git Changes", model.Conversations[0][1].Text);
    }

    [Fact]
    public async Task Existing_embedding_is_reused_instead_of_calling_the_embedder()
    {
        var embedder = new FakeEmbedder([1f, 0f, 0f]);
        var item = Item();
        item.Embedding = [0f, 1f, 0f];

        var outcome = await Pipeline(new FakeTriageModel(ValidReply), embedder)
            .RunAsync(item, [], TestContext.Current.CancellationToken);

        Assert.Equal(0, embedder.Calls);
        Assert.Equal(item.Embedding, outcome.Embedding);
    }

    [Theory]
    [InlineData("""{"area":"Licensing & Subscriptions","type":"Question","severity":3,"title":"t","summary":"s","confidence":1}""", true)]
    [InlineData("""{"area":"licensing & subscriptions","type":"feature request","severity":4,"title":"t","summary":"s","confidence":0}""", true)]
    [InlineData("""{"area":"Editor","type":"Bug","severity":"2","title":"t","summary":"s","confidence":0.5}""", false)]
    [InlineData("""{"area":"Editor","type":"Bug","severity":2,"title":"two\nlines","summary":"s","confidence":0.5}""", false)]
    [InlineData("""{"area":"Editor","type":"Bug","severity":2,"title":"t","summary":"s","confidence":1.5}""", false)]
    [InlineData("""{"area":"Editor","type":"Bug","severity":2,"title":"t","summary":"s"}""", false)]
    [InlineData("""["Editor"]""", false)]
    [InlineData("```json\n{}\n```", false)]
    public void Parser_enforces_the_contract_in_the_prompt(string reply, bool valid)
    {
        Assert.Equal(valid, TriageResponseParser.TryParse(reply, out _, out _));
    }

    [Fact]
    public void Prompt_is_loaded_from_the_versioned_file()
    {
        var prompt = TriagePrompt.Load("triage.v1");

        Assert.Equal("triage.v1", prompt.Version);
        Assert.Contains("Licensing & Subscriptions", prompt.SystemText);
    }
}
