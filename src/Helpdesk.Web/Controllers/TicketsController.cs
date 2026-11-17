using Helpdesk.Web.Models;
using Helpdesk.Web.Models.ViewModels;
using Helpdesk.Web.Security;
using Helpdesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Helpdesk.Web.Controllers;

[Authorize]
public class TicketsController : Controller
{
    private readonly TicketService _tickets;
    private readonly UserManager<ApplicationUser> _users;

    public TicketsController(TicketService tickets, UserManager<ApplicationUser> users)
    {
        _tickets = tickets;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] TicketFilter filter, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            filter = new TicketFilter();
        }
        var result = await _tickets.SearchAsync(filter, User, ct);
        return View(new TicketListViewModel { Filter = filter, Result = result });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var ticket = await _tickets.GetDetailAsync(id, User, ct);
        if (ticket is null)
        {
            // 404 rather than 403 so requesters cannot probe which ticket ids exist.
            return NotFound();
        }
        return View(BuildDetail(ticket));
    }

    [HttpGet]
    public IActionResult Create() => View(new TicketCreateModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TicketCreateModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }
        var ticket = await _tickets.CreateAsync(model, User, ct);
        TempData["Flash"] = $"Ticket #{ticket.Id} created.";
        return RedirectToAction(nameof(Details), new { id = ticket.Id });
    }

    [HttpGet]
    [Authorize(Roles = Roles.Agent)]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var ticket = await _tickets.GetDetailAsync(id, User, ct);
        if (ticket is null)
        {
            return NotFound();
        }
        var model = new TicketEditModel
        {
            Id = ticket.Id,
            Title = ticket.Title,
            Description = ticket.Description,
            Category = ticket.Category,
            Priority = ticket.Priority,
            Status = ticket.Status,
            AssigneeId = ticket.AssigneeId,
            Agents = await AgentOptionsAsync(),
        };
        return View(model);
    }

    [HttpPost]
    [Authorize(Roles = Roles.Agent)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, TicketEditModel model, CancellationToken ct)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }
        var agents = await AgentOptionsAsync();
        if (!string.IsNullOrEmpty(model.AssigneeId) && agents.All(a => a.Value != model.AssigneeId))
        {
            ModelState.AddModelError(nameof(model.AssigneeId), "Assignee must be an agent.");
        }
        if (!ModelState.IsValid)
        {
            model.Agents = agents;
            return View(model);
        }
        if (!await _tickets.UpdateAsync(model, ct))
        {
            return NotFound();
        }
        TempData["Flash"] = "Ticket updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Comment(int id, [Bind(Prefix = "NewComment")] CommentInputModel input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            var ticket = await _tickets.GetDetailAsync(id, User, ct);
            if (ticket is null)
            {
                return NotFound();
            }
            var vm = BuildDetail(ticket, input);
            return View(nameof(Details), vm);
        }
        if (!await _tickets.AddCommentAsync(id, input.Body, User, ct))
        {
            return NotFound();
        }
        TempData["Flash"] = "Reply posted.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private TicketDetailViewModel BuildDetail(Ticket ticket, CommentInputModel? input = null) => new()
    {
        Ticket = ticket,
        CanEdit = User.IsAgent(),
        CanUseTicketAssistant = User.IsAgent(),
        NewComment = input ?? new CommentInputModel(),
    };

    private async Task<IReadOnlyList<SelectListItem>> AgentOptionsAsync()
    {
        var agents = await _users.GetUsersInRoleAsync(Roles.Agent);
        return agents.OrderBy(a => a.DisplayName)
            .Select(a => new SelectListItem(a.DisplayName, a.Id))
            .ToList();
    }
}
