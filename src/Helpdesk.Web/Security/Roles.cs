using System.Security.Claims;

namespace Helpdesk.Web.Security;

public static class Roles
{
    public const string Agent = "Agent";
    public const string Requester = "Requester";

    public static readonly string[] All = { Agent, Requester };
}

public static class ClaimsPrincipalExtensions
{
    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Authenticated user has no NameIdentifier claim.");

    public static bool IsAgent(this ClaimsPrincipal user) => user.IsInRole(Roles.Agent);
}
