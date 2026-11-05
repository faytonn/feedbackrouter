using FeedbackRouter.Core.Domain;

namespace FeedbackRouter.Core.Triage;

public sealed record PoolEntry(int FeedbackItemId, string Title, float[] Embedding);

public sealed record ScoredCandidate(int FeedbackItemId, string Title, double Similarity);

/// <summary>
/// Brute-force cosine similarity over every eligible item. This is O(n) per
/// new item and fine for thousands of items; beyond that it needs a vector index.
/// </summary>
public static class DuplicateDetector
{
    public const double DefaultThreshold = 0.80;
    public const int DefaultMaxCandidates = 3;

    /// <summary>Statuses whose items can be proposed as the original of a duplicate.</summary>
    public static readonly FeedbackStatus[] EligibleStatuses =
    [
        FeedbackStatus.Proposed,
        FeedbackStatus.InReview,
        FeedbackStatus.Approved,
        FeedbackStatus.Filed,
    ];

    public static IReadOnlyList<ScoredCandidate> FindCandidates(
        int selfId,
        float[] query,
        IEnumerable<PoolEntry> pool,
        double threshold = DefaultThreshold,
        int maxCandidates = DefaultMaxCandidates)
    {
        return pool
            .Where(p => p.FeedbackItemId != selfId)
            .Select(p => new ScoredCandidate(p.FeedbackItemId, p.Title, CosineSimilarity(query, p.Embedding)))
            .Where(c => c.Similarity >= threshold)
            .OrderByDescending(c => c.Similarity)
            .ThenBy(c => c.FeedbackItemId)
            .Take(maxCandidates)
            .ToList();
    }

    public static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException($"Embedding lengths differ: {a.Length} and {b.Length}.");
        }

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
