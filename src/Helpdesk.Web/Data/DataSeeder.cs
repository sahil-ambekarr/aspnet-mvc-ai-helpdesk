using Helpdesk.Web.Models;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Data;

public sealed class DemoUserOptions
{
    public string Key { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = Roles.Requester;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Creates roles, demo users (from the "DemoUsers" config section) and fictional tickets / KB articles.
/// Only runs when "Seed:Enabled" is true, which is the case in Development and in tests.
/// </summary>
public sealed class DataSeeder
{
    // Fixed reference time so seeded data (and screenshots/tests) are reproducible.
    public static readonly DateTime SeedNow = new(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);

    private readonly HelpdeskDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly IConfiguration _config;
    private readonly ILogger<DataSeeder> _logger;

    public DataSeeder(
        HelpdeskDbContext db,
        UserManager<ApplicationUser> users,
        RoleManager<IdentityRole> roles,
        IConfiguration config,
        ILogger<DataSeeder> logger)
    {
        _db = db;
        _users = users;
        _roles = roles;
        _config = config;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        foreach (var role in Roles.All)
        {
            if (!await _roles.RoleExistsAsync(role))
            {
                await _roles.CreateAsync(new IdentityRole(role));
            }
        }

        var demoUsers = _config.GetSection("DemoUsers").Get<List<DemoUserOptions>>() ?? new();
        var idsByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var demo in demoUsers)
        {
            var user = await _users.FindByEmailAsync(demo.Email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = demo.Email,
                    Email = demo.Email,
                    EmailConfirmed = true,
                    DisplayName = demo.DisplayName,
                };
                var result = await _users.CreateAsync(user, demo.Password);
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Could not create demo user {demo.Email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
                }
                await _users.AddToRoleAsync(user, demo.Role);
            }
            idsByKey[demo.Key] = user.Id;
        }

        if (!await _db.KbArticles.AnyAsync(ct))
        {
            var updated = SeedNow.AddDays(-40);
            foreach (var article in SeedKbArticles.All)
            {
                _db.KbArticles.Add(new KbArticle
                {
                    Slug = article.Slug,
                    Title = article.Title,
                    Category = article.Category,
                    Tags = article.Tags,
                    Summary = article.Summary,
                    Body = article.Body,
                    UpdatedAt = updated,
                });
                updated = updated.AddDays(3);
            }
            await _db.SaveChangesAsync(ct);
        }

        if (await _db.Tickets.AnyAsync(ct))
        {
            return;
        }

        var needed = SeedTickets.All
            .SelectMany(t => t.Comments.Select(c => c.Author).Append(t.Requester).Append(t.Assignee ?? t.Requester))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var missing = needed.Where(k => !idsByKey.ContainsKey(k)).ToList();
        if (missing.Count > 0)
        {
            _logger.LogWarning("Skipping ticket seed; demo users not configured: {Missing}", string.Join(", ", missing));
            return;
        }

        var hourOffset = 0;
        foreach (var seed in SeedTickets.All)
        {
            // Spread creation times across the working day so lists do not show identical timestamps.
            var created = SeedNow.AddDays(-seed.DaysAgo).AddHours(-(hourOffset++ % 7));
            var ticket = new Ticket
            {
                Title = seed.Title,
                Description = seed.Description,
                Category = seed.Category,
                Priority = seed.Priority,
                Status = seed.Status,
                RequesterId = idsByKey[seed.Requester],
                AssigneeId = seed.Assignee is null ? null : idsByKey[seed.Assignee],
                CreatedAt = created,
                UpdatedAt = created,
            };
            foreach (var c in seed.Comments)
            {
                var at = created.AddHours(c.HoursAfterCreate);
                ticket.Comments.Add(new TicketComment { AuthorId = idsByKey[c.Author], Body = c.Body, CreatedAt = at });
                ticket.UpdatedAt = at;
            }
            if (seed.Status is TicketStatus.Resolved or TicketStatus.Closed)
            {
                ticket.ResolvedAt = ticket.UpdatedAt;
            }
            _db.Tickets.Add(ticket);
        }
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Tickets} tickets and {Articles} KB articles", SeedTickets.All.Count, SeedKbArticles.All.Count);
    }
}
