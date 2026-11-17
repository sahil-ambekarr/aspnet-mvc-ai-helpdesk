using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Helpdesk.Web.Models.ViewModels;

public sealed class TicketFilter
{
    public const int PageSize = 10;

    public TicketStatus? Status { get; set; }
    public TicketPriority? Priority { get; set; }
    public TicketCategory? Category { get; set; }

    [StringLength(100)]
    public string? Q { get; set; }

    /// <summary>Agents only: "all", "mine" or "unassigned".</summary>
    public string? Scope { get; set; }

    [Range(1, 10_000)]
    public int Page { get; set; } = 1;
}

public sealed record TicketListItem(
    int Id,
    string Title,
    TicketCategory Category,
    TicketPriority Priority,
    TicketStatus Status,
    string RequesterName,
    string? AssigneeName,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public sealed class TicketListViewModel
{
    public required TicketFilter Filter { get; init; }
    public required PagedResult<TicketListItem> Result { get; init; }
}

public sealed class TicketCreateModel
{
    [Required, StringLength(200, MinimumLength = 5)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(8000, MinimumLength = 10)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public TicketCategory? Category { get; set; }

    [Required]
    public TicketPriority? Priority { get; set; } = TicketPriority.Medium;
}

public sealed class TicketEditModel
{
    public int Id { get; set; }

    [Required, StringLength(200, MinimumLength = 5)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(8000, MinimumLength = 10)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public TicketCategory? Category { get; set; }

    [Required]
    public TicketPriority? Priority { get; set; }

    [Required]
    public TicketStatus? Status { get; set; }

    [Display(Name = "Assignee")]
    public string? AssigneeId { get; set; }

    public IReadOnlyList<SelectListItem> Agents { get; set; } = Array.Empty<SelectListItem>();
}

public sealed class CommentInputModel
{
    [Required, StringLength(4000, MinimumLength = 2)]
    [Display(Name = "Reply")]
    public string Body { get; set; } = string.Empty;
}

public sealed class TicketDetailViewModel
{
    public required Ticket Ticket { get; init; }
    public required bool CanEdit { get; init; }
    public required bool CanUseTicketAssistant { get; init; }
    public CommentInputModel NewComment { get; init; } = new();
}

public sealed record CountRow(string Label, int Count);

public sealed class DashboardViewModel
{
    public required IReadOnlyList<CountRow> ByStatus { get; init; }
    public required IReadOnlyList<CountRow> ByPriority { get; init; }
    public required int OpenCount { get; init; }
    public required int UnassignedCount { get; init; }
    public required int UrgentOpenCount { get; init; }
    public required int ResolvedLast7Days { get; init; }
    public required IReadOnlyList<TicketListItem> Recent { get; init; }
    public IReadOnlyList<CountRow> AiUsage { get; init; } = Array.Empty<CountRow>();
    public bool IsAgentView { get; init; }
}

public sealed class LoginViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}
