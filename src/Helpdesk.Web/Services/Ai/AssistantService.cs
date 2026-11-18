using System.Diagnostics;
using System.Security.Claims;
using Helpdesk.Web.Data;
using Helpdesk.Web.Models;
using Helpdesk.Web.Security;
using Helpdesk.Web.Services.Kb;

namespace Helpdesk.Web.Services.Ai;

/// <param name="Number">Citation number used in the answer text ([1], [2]...); 0 when not a KB answer citation.</param>
public sealed record AssistantSource(int Id, string Title, string Url, double Score, int Number = 0);

public sealed record AssistantResult(
    AiInteractionType Type,
    string Text,
    bool Found,
    IReadOnlyList<AssistantSource> Sources,
    string Provider,
    string Model,
    long LatencyMs,
    long? InteractionId);

public enum AssistantErrorKind
{
    NotFound,
    ProviderFailed,
}

public sealed class AssistantException : Exception
{
    public AssistantException(AssistantErrorKind kind, string message, Exception? inner = null) : base(message, inner) => Kind = kind;

    public AssistantErrorKind Kind { get; }
}

/// <summary>
/// The three assistant features. Every LLM call is timed and written to the AiInteractions audit table,
/// including failures. Nothing here sends anything to a requester: reply drafts are returned to the agent only.
/// </summary>
public sealed class AssistantService
{
    public const string NotFoundMessage = "Not found in the knowledge base. Try different words, or raise a ticket so an agent can help.";

    private readonly ILlmProvider _llm;
    private readonly KbSearchService _kb;
    private readonly TicketService _tickets;
    private readonly HelpdeskDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<AssistantService> _logger;

    public AssistantService(
        ILlmProvider llm,
        KbSearchService kb,
        TicketService tickets,
        HelpdeskDbContext db,
        TimeProvider clock,
        ILogger<AssistantService> logger)
    {
        _llm = llm;
        _kb = kb;
        _tickets = tickets;
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AssistantResult> AnswerFromKbAsync(string question, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var hits = await _kb.SearchAsync(question, top: 3, ct);
        if (hits.Count == 0)
        {
            // No retrieval match: answer directly without spending an LLM call.
            return new AssistantResult(AiInteractionType.KbAnswer, NotFoundMessage, false, Array.Empty<AssistantSource>(),
                _llm.Name, _llm.Model, 0, null);
        }

        var (result, latency, interactionId) = await RunAsync(PromptBuilder.KbAnswer(question, hits), null, question, user, ct);

        if (result.Text.Contains(PromptBuilder.NotFoundToken, StringComparison.Ordinal))
        {
            return new AssistantResult(AiInteractionType.KbAnswer, NotFoundMessage, false, Array.Empty<AssistantSource>(),
                _llm.Name, result.Model, latency, interactionId);
        }

        // Link only the articles the answer cites; fall back to all retrieved ones if it cites none.
        var numbered = hits.Select((h, i) => new AssistantSource(h.Article.Id, h.Article.Title, $"/kb/{h.Article.Slug}", h.Score, i + 1)).ToList();
        var cited = numbered.Where(s => result.Text.Contains($"[{s.Number}]", StringComparison.Ordinal)).ToList();
        var sources = cited.Count > 0 ? cited : numbered;
        return new AssistantResult(AiInteractionType.KbAnswer, result.Text, true, sources, _llm.Name, result.Model, latency, interactionId);
    }

    public async Task<AssistantResult> SummarizeTicketAsync(int ticketId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var ticket = await LoadTicketAsync(ticketId, user, ct);
        var (result, latency, id) = await RunAsync(PromptBuilder.TicketSummary(ticket), ticket.Id, $"ticket #{ticket.Id}", user, ct);
        return new AssistantResult(AiInteractionType.TicketSummary, result.Text, true, Array.Empty<AssistantSource>(),
            _llm.Name, result.Model, latency, id);
    }

    public async Task<AssistantResult> DraftReplyAsync(int ticketId, ClaimsPrincipal user, string agentName, CancellationToken ct = default)
    {
        var ticket = await LoadTicketAsync(ticketId, user, ct);
        var related = await _kb.SearchAsync($"{ticket.Title} {ticket.Description}", top: 2, ct);
        var request = PromptBuilder.ReplyDraft(ticket, agentName, related);
        var (result, latency, id) = await RunAsync(request, ticket.Id, $"ticket #{ticket.Id}", user, ct);
        var sources = related.Select(h => new AssistantSource(h.Article.Id, h.Article.Title, $"/kb/{h.Article.Slug}", h.Score)).ToList();
        return new AssistantResult(AiInteractionType.ReplyDraft, result.Text, true, sources, _llm.Name, result.Model, latency, id);
    }

    private async Task<Ticket> LoadTicketAsync(int ticketId, ClaimsPrincipal user, CancellationToken ct) =>
        await _tickets.GetDetailAsync(ticketId, user, ct)
        ?? throw new AssistantException(AssistantErrorKind.NotFound, $"Ticket {ticketId} not found.");

    private async Task<(LlmResult Result, long LatencyMs, long InteractionId)> RunAsync(
        LlmRequest request, int? ticketId, string input, ClaimsPrincipal user, CancellationToken ct)
    {
        var audit = new AiInteraction
        {
            Type = request.Feature,
            TicketId = ticketId,
            Provider = _llm.Name,
            Model = _llm.Model,
            Input = Clip(input, 1000),
            CreatedById = user.GetUserId(),
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };

        var sw = Stopwatch.StartNew();
        try
        {
            var raw = await _llm.CompleteAsync(request, ct);
            sw.Stop();
            var result = raw with { Text = PromptBuilder.CleanOutput(raw.Text) };

            audit.Succeeded = true;
            audit.Model = Clip(result.Model, 80);
            audit.LatencyMs = sw.ElapsedMilliseconds;
            audit.InputTokens = result.InputTokens;
            audit.OutputTokens = result.OutputTokens;
            audit.Output = Clip(result.Text, 8000);
            await SaveAuditAsync(audit);

            _logger.LogInformation(
                "AI {Feature} via {Provider}/{Model} took {LatencyMs} ms (in={InputTokens}, out={OutputTokens})",
                request.Feature, _llm.Name, result.Model, sw.ElapsedMilliseconds, result.InputTokens, result.OutputTokens);
            return (result, sw.ElapsedMilliseconds, audit.Id);
        }
        catch (LlmException ex)
        {
            sw.Stop();
            audit.Succeeded = false;
            audit.LatencyMs = sw.ElapsedMilliseconds;
            audit.Error = Clip(ex.Message, 500);
            await SaveAuditAsync(audit);
            _logger.LogWarning(ex, "AI {Feature} via {Provider} failed after {LatencyMs} ms", request.Feature, _llm.Name, sw.ElapsedMilliseconds);
            throw new AssistantException(AssistantErrorKind.ProviderFailed, "The AI assistant is unavailable right now. Please try again.", ex);
        }
    }

    private async Task SaveAuditAsync(AiInteraction audit)
    {
        // Not tied to the request token: the audit row should be written even if the client disconnected.
        _db.AiInteractions.Add(audit);
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max];
}
