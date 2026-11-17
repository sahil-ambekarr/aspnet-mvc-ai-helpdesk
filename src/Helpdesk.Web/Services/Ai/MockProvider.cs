using System.Text.RegularExpressions;
using Helpdesk.Web.Models;

namespace Helpdesk.Web.Services.Ai;

/// <summary>
/// Deterministic canned responses for tests, CI and offline demos. It reads a few labelled lines that
/// <see cref="PromptBuilder"/> puts into the prompt so the output looks related to the ticket. Same input, same output.
/// </summary>
public sealed partial class MockProvider : ILlmProvider
{
    public string Name => "mock";

    public string Model => "mock-1";

    public Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var prompt = request.UserPrompt;
        var text = request.Feature switch
        {
            AiInteractionType.KbAnswer => KbAnswer(prompt),
            AiInteractionType.TicketSummary => Summary(prompt),
            AiInteractionType.ReplyDraft => ReplyDraft(prompt),
            _ => "[mock] Unsupported feature.",
        };
        var words = prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return Task.FromResult(new LlmResult(text, Model, words, text.Split(' ').Length));
    }

    private static string KbAnswer(string prompt)
    {
        var sources = SourceLine().Matches(prompt).Select(m => m.Groups[1].Value.Trim()).ToList();
        if (sources.Count == 0)
        {
            return PromptBuilder.NotFoundToken;
        }
        return $"[mock] The knowledge base covers this in [1] {sources[0]}. Follow the steps in that article"
            + (sources.Count > 1 ? $"; [2] {sources[1]} may also help." : ".");
    }

    private static string Summary(string prompt)
    {
        var title = Field(prompt, "Title") ?? "this ticket";
        var status = Field(prompt, "Status") ?? "unknown";
        var comments = CommentLine().Matches(prompt).Count;
        return $"""
            [mock] - Issue: {title}
            - Status: {status}, {comments} comment(s) in the thread
            - Next step: agent to review the latest comment and reply
            """;
    }

    private static string ReplyDraft(string prompt)
    {
        var requester = (Field(prompt, "Requester") ?? "there").Split(' ')[0];
        var title = Field(prompt, "Title") ?? "your ticket";
        var agent = Field(prompt, "Replying agent") ?? "IT Service Desk";
        return $"""
            [mock] Hi {requester},

            Thanks for the update on "{title}". I am looking into this now and will get back to you with the next step today.

            Best regards,
            {agent}
            """;
    }

    private static string? Field(string prompt, string label)
    {
        var match = Regex.Match(prompt, $"^{Regex.Escape(label)}: (.+)$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    [GeneratedRegex(@"^\[\d+\] Title: (.+)$", RegexOptions.Multiline)]
    private static partial Regex SourceLine();

    [GeneratedRegex(@"^--- Comment \d+", RegexOptions.Multiline)]
    private static partial Regex CommentLine();
}
