using FeedbackRouter.Core.Triage;

namespace FeedbackRouter.Tests.Triage;

public class DuplicateDetectorTests
{
    [Fact]
    public void Cosine_similarity_of_known_vectors()
    {
        Assert.Equal(1.0, DuplicateDetector.CosineSimilarity([1f, 2f, 3f], [2f, 4f, 6f]), 6);
        Assert.Equal(0.0, DuplicateDetector.CosineSimilarity([1f, 0f], [0f, 1f]), 6);
        Assert.Equal(-1.0, DuplicateDetector.CosineSimilarity([1f, 0f], [-1f, 0f]), 6);
        Assert.Equal(0.0, DuplicateDetector.CosineSimilarity([0f, 0f], [1f, 1f]), 6);
    }

    [Fact]
    public void Mismatched_lengths_throw()
    {
        Assert.Throws<ArgumentException>(() => DuplicateDetector.CosineSimilarity([1f], [1f, 0f]));
    }

    [Fact]
    public void Keeps_top_three_above_threshold_in_order_and_excludes_self()
    {
        float[] query = [1f, 0f];

        // cos(angle) for each vector against [1, 0].
        var pool = new[]
        {
            new PoolEntry(1, "self", [1f, 0f]),
            new PoolEntry(2, "0.95", [0.95f, MathF.Sqrt(1 - 0.95f * 0.95f)]),
            new PoolEntry(3, "0.81", [0.81f, MathF.Sqrt(1 - 0.81f * 0.81f)]),
            new PoolEntry(4, "0.79", [0.79f, MathF.Sqrt(1 - 0.79f * 0.79f)]),
            new PoolEntry(5, "0.99", [0.99f, MathF.Sqrt(1 - 0.99f * 0.99f)]),
            new PoolEntry(6, "0.90", [0.90f, MathF.Sqrt(1 - 0.90f * 0.90f)]),
        };

        var result = DuplicateDetector.FindCandidates(selfId: 1, query, pool);

        Assert.Equal([5, 2, 6], result.Select(r => r.FeedbackItemId));
        Assert.Equal(0.99, result[0].Similarity, 4);
    }

    [Fact]
    public void Nothing_above_threshold_returns_empty()
    {
        var pool = new[] { new PoolEntry(2, "orthogonal", [0f, 1f]) };

        Assert.Empty(DuplicateDetector.FindCandidates(1, [1f, 0f], pool));
    }

    [Fact]
    public void Threshold_is_inclusive()
    {
        // 4 / sqrt(4^2 + 3^2) is exactly 0.8 in double arithmetic.
        var pool = new[] { new PoolEntry(2, "exactly 0.8", [4f, 3f]) };

        Assert.Single(DuplicateDetector.FindCandidates(1, [1f, 0f], pool));
    }
}
