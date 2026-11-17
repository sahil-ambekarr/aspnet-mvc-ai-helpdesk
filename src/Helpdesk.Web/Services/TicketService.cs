using System.Security.Claims;
using Helpdesk.Web.Data;
using Helpdesk.Web.Models;
using Helpdesk.Web.Models.ViewModels;
using Helpdesk.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Services;

/// <summary>
/// Ticket queries and commands. All reads go through <see cref="VisibleTo"/> so a requester can only
/// ever load their own tickets; agents see everything.
/// </summary>
public sealed class TicketService
{
    private readonly HelpdeskDbContext _db;
    private readonly TimeProvider _clock;

    public TicketService(HelpdeskDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public IQueryable<Ticket> VisibleTo(ClaimsPrincipal user)
    {
        var query = _db.Tickets.AsQueryable();
        if (!user.IsAgent())
        {
            var userId = user.GetUserId();
            query = query.Where(t => t.RequesterId == userId);
        }
        return query;
    }

    public async Task<PagedResult<TicketListItem>> SearchAsync(TicketFilter filter, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var query = VisibleTo(user);

        if (filter.Status is { } status)
        {
            query = query.Where(t => t.Status == status);
        }
        if (filter.Priority is { } priority)
        {
            query = query.Where(t => t.Priority == priority);
        }
        if (filter.Category is { } category)
        {
            query = query.Where(t => t.Category == category);
        }
        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            var q = filter.Q.Trim();
            // LIKE is case-insensitive on both SQLite (ASCII) and the default SQL Server collation;
            // string.Contains would translate to a case-sensitive instr() on SQLite.
            var pattern = "%" + EscapeLike(q) + "%";
            query = int.TryParse(q.TrimStart('#'), out var id)
                ? query.Where(t => t.Id == id || EF.Functions.Like(t.Title, pattern, "\\"))
                : query.Where(t => EF.Functions.Like(t.Title, pattern, "\\") || EF.Functions.Like(t.Description, pattern, "\\"));
        }
        if (user.IsAgent())
        {
            var userId = user.GetUserId();
            query = filter.Scope switch
            {
                "mine" => query.Where(t => t.AssigneeId == userId),
                "unassigned" => query.Where(t => t.AssigneeId == null),
                _ => query,
            };
        }

        var total = await query.CountAsync(ct);
        var page = Math.Max(1, filter.Page);
        var items = await Project(query
                .OrderByDescending(t => t.UpdatedAt)
                .ThenByDescending(t => t.Id)
                .Skip((page - 1) * TicketFilter.PageSize)
                .Take(TicketFilter.PageSize))
            .ToListAsync(ct);

        return new PagedResult<TicketListItem>
        {
            Items = items,
            Page = page,
            PageSize = TicketFilter.PageSize,
            TotalCount = total,
        };
    }

    public Task<Ticket?> GetDetailAsync(int id, ClaimsPrincipal user, CancellationToken ct = default) =>
        VisibleTo(user)
            .Include(t => t.Requester)
            .Include(t => t.Assignee)
            .Include(t => t.Comments.OrderBy(c => c.CreatedAt))
                .ThenInclude(c => c.Author)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<Ticket> CreateAsync(TicketCreateModel model, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var ticket = new Ticket
        {
            Title = model.Title.Trim(),
            Description = model.Description.Trim(),
            Category = model.Category!.Value,
            Priority = model.Priority!.Value,
            Status = TicketStatus.New,
            RequesterId = user.GetUserId(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync(ct);
        return ticket;
    }

    /// <summary>Agent-only update. Returns false if the ticket does not exist.</summary>
    public async Task<bool> UpdateAsync(TicketEditModel model, CancellationToken ct = default)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == model.Id, ct);
        if (ticket is null)
        {
            return false;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var newStatus = model.Status!.Value;
        if (newStatus is TicketStatus.Resolved or TicketStatus.Closed)
        {
            ticket.ResolvedAt ??= now;
        }
        else
        {
            ticket.ResolvedAt = null;
        }

        ticket.Title = model.Title.Trim();
        ticket.Description = model.Description.Trim();
        ticket.Category = model.Category!.Value;
        ticket.Priority = model.Priority!.Value;
        ticket.Status = newStatus;
        ticket.AssigneeId = string.IsNullOrWhiteSpace(model.AssigneeId) ? null : model.AssigneeId;
        ticket.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Adds a comment if the user can see the ticket. Returns false otherwise.</summary>
    public async Task<bool> AddCommentAsync(int ticketId, string body, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var ticket = await VisibleTo(user).FirstOrDefaultAsync(t => t.Id == ticketId, ct);
        if (ticket is null)
        {
            return false;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var userId = user.GetUserId();
        ticket.Comments.Add(new TicketComment { AuthorId = userId, Body = body.Trim(), CreatedAt = now });
        ticket.UpdatedAt = now;

        // A requester replying to a "waiting on requester" ticket hands it back to the agent.
        if (!user.IsAgent() && ticket.Status == TicketStatus.WaitingOnRequester)
        {
            ticket.Status = TicketStatus.Open;
        }
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<DashboardViewModel> GetDashboardAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var visible = VisibleTo(user);

        var byStatus = await visible.GroupBy(t => t.Status)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var byPriority = await visible.Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
            .GroupBy(t => t.Priority)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);

        var now = _clock.GetUtcNow().UtcDateTime;
        var weekAgo = now.AddDays(-7);
        var openStatuses = new[] { TicketStatus.New, TicketStatus.Open, TicketStatus.WaitingOnRequester };

        var recent = await Project(visible.OrderByDescending(t => t.UpdatedAt).Take(6)).ToListAsync(ct);

        var aiUsage = new List<CountRow>();
        if (user.IsAgent())
        {
            var usage = await _db.AiInteractions.GroupBy(i => i.Type)
                .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            aiUsage = Enum.GetValues<AiInteractionType>()
                .Select(t => new CountRow(t.ToString(), usage.FirstOrDefault(u => u.Key == t)?.Count ?? 0))
                .ToList();
        }

        return new DashboardViewModel
        {
            ByStatus = Enum.GetValues<TicketStatus>()
                .Select(s => new CountRow(s.ToString(), byStatus.FirstOrDefault(x => x.Key == s)?.Count ?? 0)).ToList(),
            ByPriority = Enum.GetValues<TicketPriority>().Reverse()
                .Select(p => new CountRow(p.ToString(), byPriority.FirstOrDefault(x => x.Key == p)?.Count ?? 0)).ToList(),
            OpenCount = await visible.CountAsync(t => openStatuses.Contains(t.Status), ct),
            UnassignedCount = await visible.CountAsync(t => t.AssigneeId == null && openStatuses.Contains(t.Status), ct),
            UrgentOpenCount = await visible.CountAsync(t => t.Priority == TicketPriority.Urgent && openStatuses.Contains(t.Status), ct),
            ResolvedLast7Days = await visible.CountAsync(t => t.ResolvedAt != null && t.ResolvedAt >= weekAgo, ct),
            Recent = recent,
            AiUsage = aiUsage,
            IsAgentView = user.IsAgent(),
        };
    }

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace("[", @"\[");

    private static IQueryable<TicketListItem> Project(IQueryable<Ticket> query) =>
        query.Select(t => new TicketListItem(
            t.Id,
            t.Title,
            t.Category,
            t.Priority,
            t.Status,
            t.Requester!.DisplayName,
            t.Assignee == null ? null : t.Assignee.DisplayName,
            t.CreatedAt,
            t.UpdatedAt));
}
