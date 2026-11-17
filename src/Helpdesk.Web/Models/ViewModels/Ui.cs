using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Helpdesk.Web.Models.ViewModels;

/// <summary>Small display helpers shared by the Razor views.</summary>
public static class Ui
{
    public static string StatusClass(TicketStatus status) => status switch
    {
        TicketStatus.New => "text-bg-info",
        TicketStatus.Open => "text-bg-primary",
        TicketStatus.WaitingOnRequester => "text-bg-warning",
        TicketStatus.Resolved => "text-bg-success",
        _ => "text-bg-secondary",
    };

    public static string PriorityClass(TicketPriority priority) => priority switch
    {
        TicketPriority.Urgent => "text-bg-danger",
        TicketPriority.High => "text-bg-warning",
        TicketPriority.Medium => "text-bg-light border",
        _ => "text-bg-light border text-secondary",
    };

    public static string Label<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var member = typeof(TEnum).GetMember(value.ToString()).FirstOrDefault();
        return member?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? value.ToString();
    }

    public static string Label(string enumName) => enumName == nameof(TicketStatus.WaitingOnRequester)
        ? "Waiting on requester"
        : enumName;

    public static string Ago(DateTime utc, DateTime nowUtc)
    {
        var span = nowUtc - utc;
        if (span.TotalMinutes < 1)
        {
            return "just now";
        }
        if (span.TotalHours < 1)
        {
            return $"{(int)span.TotalMinutes} min ago";
        }
        if (span.TotalDays < 1)
        {
            return $"{(int)span.TotalHours} h ago";
        }
        return span.TotalDays < 30 ? $"{(int)span.TotalDays} d ago" : utc.ToString("d MMM yyyy");
    }

    public static IEnumerable<string> Paragraphs(string text) =>
        text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
