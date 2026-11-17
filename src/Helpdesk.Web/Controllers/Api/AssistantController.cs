using System.ComponentModel.DataAnnotations;
using Helpdesk.Web.Models;
using Helpdesk.Web.Security;
using Helpdesk.Web.Services.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Helpdesk.Web.Controllers.Api;

public sealed class KbQuestion
{
    [Required, StringLength(500, MinimumLength = 3)]
    public string Question { get; set; } = string.Empty;
}

/// <summary>
/// JSON API used by the assistant widget. Cookie-authenticated, so every POST also needs the antiforgery
/// token (sent in the RequestVerificationToken header by assistant.js).
/// </summary>
[ApiController]
[Route("api/assistant")]
[Authorize]
[ValidateAntiForgeryToken]
public class AssistantController : ControllerBase
{
    private readonly AssistantService _assistant;
    private readonly UserManager<ApplicationUser> _users;

    public AssistantController(AssistantService assistant, UserManager<ApplicationUser> users)
    {
        _assistant = assistant;
        _users = users;
    }

    [HttpPost("kb-answer")]
    public Task<IActionResult> KbAnswer([FromBody] KbQuestion body, CancellationToken ct) =>
        Run(() => _assistant.AnswerFromKbAsync(body.Question.Trim(), User, ct));

    [HttpPost("tickets/{id:int}/summary")]
    [Authorize(Roles = Roles.Agent)]
    public Task<IActionResult> Summary(int id, CancellationToken ct) =>
        Run(() => _assistant.SummarizeTicketAsync(id, User, ct));

    [HttpPost("tickets/{id:int}/reply-draft")]
    [Authorize(Roles = Roles.Agent)]
    public async Task<IActionResult> ReplyDraft(int id, CancellationToken ct)
    {
        var agent = await _users.GetUserAsync(User);
        var agentName = agent?.DisplayName is { Length: > 0 } name ? name : "IT Service Desk";
        return await Run(() => _assistant.DraftReplyAsync(id, User, agentName, ct));
    }

    private async Task<IActionResult> Run(Func<Task<AssistantResult>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (AssistantException ex) when (ex.Kind == AssistantErrorKind.NotFound)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: ex.Message);
        }
        catch (AssistantException ex)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: ex.Message);
        }
    }
}
