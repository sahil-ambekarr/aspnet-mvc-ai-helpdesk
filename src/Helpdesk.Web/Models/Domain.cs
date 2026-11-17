using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Helpdesk.Web.Models;

public enum TicketStatus
{
    New = 0,
    Open = 1,
    [Display(Name = "Waiting on requester")]
    WaitingOnRequester = 2,
    Resolved = 3,
    Closed = 4,
}

public enum TicketPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Urgent = 3,
}

public enum TicketCategory
{
    Hardware = 0,
    Software = 1,
    Network = 2,
    Access = 3,
    Email = 4,
    Other = 5,
}

public enum AiInteractionType
{
    KbAnswer = 0,
    TicketSummary = 1,
    ReplyDraft = 2,
}

public class ApplicationUser : IdentityUser
{
    [MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;
}

public class Ticket
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(8000)]
    public string Description { get; set; } = string.Empty;

    public TicketCategory Category { get; set; }
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    public TicketStatus Status { get; set; } = TicketStatus.New;

    [MaxLength(450)]
    public string RequesterId { get; set; } = string.Empty;
    public ApplicationUser? Requester { get; set; }

    [MaxLength(450)]
    public string? AssigneeId { get; set; }
    public ApplicationUser? Assignee { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public List<TicketComment> Comments { get; set; } = new();
}

public class TicketComment
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    [MaxLength(450)]
    public string AuthorId { get; set; } = string.Empty;
    public ApplicationUser? Author { get; set; }

    [MaxLength(4000)]
    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

public class KbArticle
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(120)]
    public string Slug { get; set; } = string.Empty;

    public TicketCategory Category { get; set; }

    [MaxLength(300)]
    public string Summary { get; set; } = string.Empty;

    /// <summary>Plain text with blank-line separated paragraphs and "- " bullet lines.</summary>
    [MaxLength(8000)]
    public string Body { get; set; } = string.Empty;

    [MaxLength(300)]
    public string Tags { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; }
}

/// <summary>Audit row written for every AI call, successful or not.</summary>
public class AiInteraction
{
    public long Id { get; set; }
    public AiInteractionType Type { get; set; }
    public int? TicketId { get; set; }

    [MaxLength(40)]
    public string Provider { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Model { get; set; } = string.Empty;

    public long LatencyMs { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public bool Succeeded { get; set; }

    [MaxLength(500)]
    public string? Error { get; set; }

    [MaxLength(1000)]
    public string? Input { get; set; }

    [MaxLength(8000)]
    public string? Output { get; set; }

    [MaxLength(450)]
    public string CreatedById { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
