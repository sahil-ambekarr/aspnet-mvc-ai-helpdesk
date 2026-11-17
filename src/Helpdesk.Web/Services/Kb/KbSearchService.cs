using System.Text.RegularExpressions;
using Helpdesk.Web.Data;
using Helpdesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Services.Kb;

public sealed record KbHit(KbArticle Article, double Score);

/// <summary>
/// Keyword retrieval over KB articles using Okapi BM25 (k1 = 1.2, b = 0.75).
/// Title and tags are counted more than once so they weigh more than body text.
/// The index is rebuilt per call: with tens to a few hundred articles this takes well under a millisecond
/// and avoids cache invalidation when an article changes. Swap in a persisted index or embeddings
/// if the KB grows into the thousands.
/// </summary>
public sealed partial class KbSearchService
{
    private const double K1 = 1.2;
    private const double B = 0.75;
    private const int TitleWeight = 3;
    private const int TagWeight = 2;

    /// <summary>Hits below this score are treated as "not found in KB".</summary>
    public const double MinScore = 1.5;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "and", "or", "but", "if", "then", "to", "of", "in", "on", "at", "for", "from", "by", "with",
        "is", "are", "was", "were", "be", "been", "am", "do", "does", "did", "i", "my", "me", "we", "our", "you",
        "your", "it", "its", "this", "that", "these", "those", "can", "could", "should", "would", "will", "how",
        "what", "when", "where", "why", "who", "which", "not", "no", "so", "as", "up", "get", "got", "need",
        "please", "there", "any", "all", "have", "has", "had", "some", "into", "about", "after", "before",
    };

    private readonly HelpdeskDbContext _db;

    public KbSearchService(HelpdeskDbContext db) => _db = db;

    public async Task<IReadOnlyList<KbHit>> SearchAsync(string query, int top = 3, CancellationToken ct = default)
    {
        var articles = await _db.KbArticles.AsNoTracking().ToListAsync(ct);
        return Rank(articles, query, top);
    }

    public static IReadOnlyList<KbHit> Rank(IReadOnlyList<KbArticle> articles, string query, int top = 3)
    {
        var queryTerms = Tokenize(query).Distinct().ToList();
        if (queryTerms.Count == 0 || articles.Count == 0)
        {
            return Array.Empty<KbHit>();
        }

        var docs = articles.Select(a => new
        {
            Article = a,
            Terms = BuildDocumentTerms(a),
        }).ToList();

        var avgLength = docs.Average(d => d.Terms.Count);
        var termFrequencies = docs.Select(d => d.Terms
            .GroupBy(t => t)
            .ToDictionary(g => g.Key, g => g.Count())).ToList();

        var n = docs.Count;
        var idf = queryTerms.ToDictionary(
            term => term,
            term =>
            {
                var df = termFrequencies.Count(tf => tf.ContainsKey(term));
                return Math.Log(1 + (n - df + 0.5) / (df + 0.5));
            });

        var hits = new List<KbHit>();
        for (var i = 0; i < docs.Count; i++)
        {
            var length = docs[i].Terms.Count;
            double score = 0;
            foreach (var term in queryTerms)
            {
                if (!termFrequencies[i].TryGetValue(term, out var tf))
                {
                    continue;
                }
                var norm = tf * (K1 + 1) / (tf + K1 * (1 - B + B * length / avgLength));
                score += idf[term] * norm;
            }
            if (score >= MinScore)
            {
                hits.Add(new KbHit(docs[i].Article, Math.Round(score, 3)));
            }
        }

        return hits.OrderByDescending(h => h.Score).ThenBy(h => h.Article.Id).Take(top).ToList();
    }

    private static List<string> BuildDocumentTerms(KbArticle a)
    {
        var terms = new List<string>();
        var title = Tokenize(a.Title).ToList();
        for (var i = 0; i < TitleWeight; i++)
        {
            terms.AddRange(title);
        }
        var tags = Tokenize(a.Tags).ToList();
        for (var i = 0; i < TagWeight; i++)
        {
            terms.AddRange(tags);
        }
        terms.AddRange(Tokenize(a.Summary));
        terms.AddRange(Tokenize(a.Body));
        return terms;
    }

    public static IEnumerable<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }
        foreach (Match m in WordRegex().Matches(text.ToLowerInvariant()))
        {
            // "Wi-Fi" and "wifi", "e-mail" and "email" should match.
            var word = m.Value.Replace("-", string.Empty);
            if (word.Length < 2 || StopWords.Contains(word))
            {
                continue;
            }
            yield return Stem(word);
        }
    }

    /// <summary>Very small suffix stripper so "printers"/"printing" match "printer"/"print". Not a full Porter stemmer.</summary>
    internal static string Stem(string word)
    {
        if (word.Length > 5 && word.EndsWith("ing", StringComparison.Ordinal))
        {
            return word[..^3];
        }
        if (word.Length > 4 && word.EndsWith("ed", StringComparison.Ordinal))
        {
            return word[..^2];
        }
        if (word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal))
        {
            return word[..^3] + "y";
        }
        if (word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal))
        {
            return word[..^1];
        }
        return word;
    }

    [GeneratedRegex("[a-z0-9]+(?:-[a-z0-9]+)*")]
    private static partial Regex WordRegex();
}
