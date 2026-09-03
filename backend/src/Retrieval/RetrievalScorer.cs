using System.Security.Cryptography;

namespace LiaraDocsAssistant.Retrieval;

public static class RetrievalScorer
{
    public const double TitleBoostWeight = 0.1;

    /// <summary>
    /// Final ranking score: cosine similarity (primary signal) plus a small
    /// pg_trgm title-similarity boost used as a tie-breaker. The boost weight
    /// is deliberately small so the groundedness threshold keeps measuring
    /// cosine similarity, not the boost.
    /// </summary>
    public static double Combine(double cosineSimilarity, double titleTrigramSimilarity) =>
        cosineSimilarity + (TitleBoostWeight * Math.Clamp(titleTrigramSimilarity, 0, 1));

    /// <summary>
    /// Trigram (3-gram) similarity in [0,1] between two strings — the same
    /// definition pg_trgm's similarity() uses: |shared trigrams| / |unique
    /// trigram union|. Used for the in-memory title boost over the candidate
    /// pool fetched from PostgreSQL.
    /// </summary>
    public static double TrigramSimilarity(string a, string b)
    {
        var setA = Trigrams(a);
        if (setA.Count == 0) return 0;
        var setB = Trigrams(b);
        if (setB.Count == 0) return 0;

        var shared = setA.Count(setB.Contains);
        return (double)shared / (setA.Count + setB.Count - shared);
    }

    private static HashSet<string> Trigrams(string text)
    {
        var normalized = $"  {text.Trim().ToLowerInvariant()} ";
        var result = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 3 <= normalized.Length; i++)
        {
            result.Add(normalized.Substring(i, 3));
        }
        return result;
    }

    public static string Sha256Hex(string input) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input)));
}
