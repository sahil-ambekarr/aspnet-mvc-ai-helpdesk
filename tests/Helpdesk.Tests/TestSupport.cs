using System.Net;
using System.Security.Claims;
using Helpdesk.Web.Data;
using Helpdesk.Web.Models;
using Helpdesk.Web.Security;
using Helpdesk.Web.Services.Ai;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Tests;

/// <summary>Records requests and replays queued responses.</summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

    public FakeHttpMessageHandler Enqueue(HttpStatusCode status, string body, string mediaType = "application/json")
    {
        _responses.Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType),
        });
        return this;
    }

    public FakeHttpMessageHandler EnqueueException(Exception ex)
    {
        _responses.Enqueue(_ => throw ex);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No fake response queued.");
        }
        return _responses.Dequeue()(request);
    }
}

/// <summary>An ILlmProvider double that counts calls and can be told to fail.</summary>
public sealed class CountingProvider : ILlmProvider
{
    private readonly MockProvider _inner = new();

    public int Calls { get; private set; }
    public bool Fail { get; set; }
    public LlmRequest? LastRequest { get; private set; }
    public string? FixedText { get; set; }

    public string Name => "counting";
    public string Model => "counting-1";

    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        Calls++;
        LastRequest = request;
        if (Fail)
        {
            throw new LlmException("simulated provider failure");
        }
        var result = await _inner.CompleteAsync(request, ct);
        return FixedText is null ? result : result with { Text = FixedText };
    }
}

/// <summary>SQLite in-memory database with a small fixed dataset: two agents, two requesters, three tickets.</summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var db = CreateContext();
        db.Database.EnsureCreated();
        Seed(db);
    }

    public static ApplicationUser Agent { get; } = new() { Id = "agent-1", UserName = "agent@test", Email = "agent@test", DisplayName = "Alex Agent" };
    public static ApplicationUser RequesterA { get; } = new() { Id = "req-a", UserName = "a@test", Email = "a@test", DisplayName = "Ann Requester" };
    public static ApplicationUser RequesterB { get; } = new() { Id = "req-b", UserName = "b@test", Email = "b@test", DisplayName = "Ben Requester" };

    public HelpdeskDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<HelpdeskDbContext>().UseSqlite(_connection).Options);

    public static ClaimsPrincipal Principal(ApplicationUser user, string role) =>
        new(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.Email!),
                new Claim(ClaimTypes.Role, role),
            },
            authenticationType: "Test"));

    public static ClaimsPrincipal AgentPrincipal => Principal(Agent, Roles.Agent);
    public static ClaimsPrincipal RequesterAPrincipal => Principal(RequesterA, Roles.Requester);
    public static ClaimsPrincipal RequesterBPrincipal => Principal(RequesterB, Roles.Requester);

    private static void Seed(HelpdeskDbContext db)
    {
        db.Users.AddRange(Clone(Agent), Clone(RequesterA), Clone(RequesterB));
        var t0 = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);

        db.Tickets.AddRange(
            new Ticket
            {
                Id = 1,
                Title = "VPN drops every hour",
                Description = "The VPN disconnects every 60 minutes from home.",
                Category = TicketCategory.Network,
                Priority = TicketPriority.High,
                Status = TicketStatus.Open,
                RequesterId = RequesterA.Id,
                AssigneeId = Agent.Id,
                CreatedAt = t0,
                UpdatedAt = t0.AddHours(3),
                Comments =
                {
                    new TicketComment { AuthorId = Agent.Id, Body = "Which client version?", CreatedAt = t0.AddHours(1) },
                    new TicketComment { AuthorId = RequesterA.Id, Body = "Version 5.0.4", CreatedAt = t0.AddHours(3) },
                },
            },
            new Ticket
            {
                Id = 2,
                Title = "Printer offline",
                Description = "PRN-2F-EAST shows offline for everyone.",
                Category = TicketCategory.Hardware,
                Priority = TicketPriority.Medium,
                Status = TicketStatus.New,
                RequesterId = RequesterB.Id,
                CreatedAt = t0.AddDays(1),
                UpdatedAt = t0.AddDays(1),
            },
            new Ticket
            {
                Id = 3,
                Title = "Password locked",
                Description = "Account locks after password change.",
                Category = TicketCategory.Access,
                Priority = TicketPriority.Urgent,
                Status = TicketStatus.Resolved,
                RequesterId = RequesterA.Id,
                AssigneeId = Agent.Id,
                CreatedAt = t0.AddDays(2),
                UpdatedAt = t0.AddDays(2),
                ResolvedAt = t0.AddDays(2),
            });

        var id = 1;
        foreach (var a in SeedKbArticles.All)
        {
            db.KbArticles.Add(new KbArticle
            {
                Id = id++,
                Slug = a.Slug,
                Title = a.Title,
                Category = a.Category,
                Tags = a.Tags,
                Summary = a.Summary,
                Body = a.Body,
                UpdatedAt = t0,
            });
        }
        db.SaveChanges();
    }

    private static ApplicationUser Clone(ApplicationUser u) => new()
    {
        Id = u.Id,
        UserName = u.UserName,
        NormalizedUserName = u.UserName!.ToUpperInvariant(),
        Email = u.Email,
        NormalizedEmail = u.Email!.ToUpperInvariant(),
        DisplayName = u.DisplayName,
    };

    public void Dispose() => _connection.Dispose();
}
