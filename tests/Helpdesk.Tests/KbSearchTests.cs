using Helpdesk.Web.Data;
using Helpdesk.Web.Models;
using Helpdesk.Web.Services.Kb;

namespace Helpdesk.Tests;

public class KbSearchTests
{
    private static readonly IReadOnlyList<KbArticle> Articles =
        SeedKbArticles.All.Select((a, i) => new KbArticle
        {
            Id = i + 1,
            Slug = a.Slug,
            Title = a.Title,
            Tags = a.Tags,
            Summary = a.Summary,
            Body = a.Body,
            Category = a.Category,
        }).ToList();

    [Theory]
    [InlineData("I forgot my password and my account is locked", "reset-your-network-password")]
    [InlineData("VPN keeps disconnecting when I work from home", "connect-to-the-vpn")]
    [InlineData("how do I add a printer", "add-a-network-printer")]
    [InlineData("I got a new phone and need the authenticator app", "set-up-multi-factor-authentication")]
    [InlineData("wifi access for visitors", "office-wifi-networks")]
    [InlineData("people hear an echo in my video meeting", "fix-video-meeting-audio")]
    [InlineData("suspicious email asking me to pay an invoice", "report-a-phishing-email")]
    [InlineData("send as permission for the team shared mailbox", "shared-mailbox-access")]
    [InlineData("my laptop screen is cracked", "request-new-or-replacement-laptop")]
    public void Top_hit_is_the_expected_article(string query, string expectedSlug)
    {
        var hits = KbSearchService.Rank(Articles, query);

        Assert.NotEmpty(hits);
        Assert.Equal(expectedSlug, hits[0].Article.Slug);
    }

    [Theory]
    [InlineData("what is on the cafeteria lunch menu")]
    [InlineData("the the and of")]
    [InlineData("")]
    public void Unrelated_or_empty_queries_return_no_hits(string query)
    {
        Assert.Empty(KbSearchService.Rank(Articles, query));
    }

    [Fact]
    public void Results_are_ordered_by_score_and_limited_to_top()
    {
        var hits = KbSearchService.Rank(Articles, "password access email phone", top: 3);

        Assert.InRange(hits.Count, 1, 3);
        Assert.Equal(hits.OrderByDescending(h => h.Score).Select(h => h.Article.Id), hits.Select(h => h.Article.Id));
    }

    [Fact]
    public void Title_match_outranks_a_body_only_mention()
    {
        // "laptop" appears in several article bodies but in only one title.
        var hits = KbSearchService.Rank(Articles, "laptop");
        Assert.Equal("request-new-or-replacement-laptop", hits[0].Article.Slug);
    }

    [Theory]
    [InlineData("printers", "printer")]
    [InlineData("printing", "print")]
    [InlineData("policies", "policy")]
    [InlineData("access", "access")]
    public void Stemmer_strips_common_suffixes(string word, string expected)
    {
        Assert.Equal(expected, KbSearchService.Stem(word));
    }

    [Fact]
    public void Tokenizer_drops_stop_words_and_joins_hyphenated_words()
    {
        var tokens = KbSearchService.Tokenize("How do I connect to the Wi-Fi?").ToList();
        Assert.Equal(new[] { "connect", "wifi" }, tokens);
    }
}
