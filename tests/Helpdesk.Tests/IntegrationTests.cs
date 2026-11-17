using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Helpdesk.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Helpdesk.Tests;

/// <summary>
/// Boots the real app (Development config, so demo users and seed data load) against a SQLite in-memory
/// database and the mock LLM provider.
/// </summary>
public sealed class HelpdeskAppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public HelpdeskAppFactory() => _connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LLM_PROVIDER"] = "mock",
            ["Seed:Enabled"] = "true",
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<HelpdeskDbContext>>();
            services.AddDbContext<HelpdeskDbContext>(o => o.UseSqlite(_connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection.Dispose();
    }
}

public class IntegrationTests : IClassFixture<HelpdeskAppFactory>
{
    private const string AgentEmail = "priya.agent@lakeview.example";
    private const string AgentPassword = "DemoAgent#2026";
    private const string RequesterPassword = "DemoUser#2026";

    private readonly HelpdeskAppFactory _factory;

    public IntegrationTests(HelpdeskAppFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string ExtractToken(string html) =>
        Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

    private async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = NewClient();
        var page = await client.GetStringAsync("/Account/Login");
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = ExtractToken(page),
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private async Task<int> TicketIdForRequesterAsync(string email, bool own)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        return await db.Tickets.Where(t => own ? t.RequesterId == user.Id : t.RequesterId != user.Id)
            .OrderBy(t => t.Id).Select(t => t.Id).FirstAsync();
    }

    private static async Task<HttpResponseMessage> PostApiAsync(HttpClient client, string tokenPage, string url, object? body = null)
    {
        var html = await client.GetStringAsync(tokenPage);
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Add("RequestVerificationToken", ExtractToken(html));
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Anonymous_users_are_sent_to_login_and_api_returns_401()
    {
        var client = NewClient();

        var page = await client.GetAsync("/Tickets");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Contains("/Account/Login", page.Headers.Location!.ToString());

        var api = await client.PostAsync("/api/assistant/kb-answer", JsonContent.Create(new { question = "vpn" }));
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var client = NewClient();
        var page = await client.GetStringAsync("/Account/Login");
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "dana@lakeview.example",
            ["Password"] = "not-the-password",
            ["__RequestVerificationToken"] = ExtractToken(page),
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid email or password", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Agent_sees_seeded_dashboard_and_ticket_list()
    {
        var client = await LoginAsync(AgentEmail, AgentPassword);

        var dashboard = await client.GetStringAsync("/");
        Assert.Contains("Tickets by status", dashboard);

        int total, urgent;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
            total = await db.Tickets.CountAsync();
            urgent = await db.Tickets.CountAsync(t => t.Priority == Helpdesk.Web.Models.TicketPriority.Urgent);
        }
        Assert.True(total >= 30, "seed data should contain about 30 tickets");

        var list = await client.GetStringAsync("/Tickets");
        Assert.Contains($"{total} ticket(s)", list);
        Assert.Contains($"Page 1 of {(total + 9) / 10}", list);

        var filtered = await client.GetStringAsync("/Tickets?priority=Urgent");
        Assert.Contains($"{urgent} ticket(s)", filtered);
    }

    [Fact]
    public async Task Requester_cannot_see_another_requesters_ticket()
    {
        const string dana = "dana@lakeview.example";
        var client = await LoginAsync(dana, RequesterPassword);
        var othersTicket = await TicketIdForRequesterAsync(dana, own: false);
        var ownTicket = await TicketIdForRequesterAsync(dana, own: true);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Tickets/Details/{othersTicket}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Tickets/Details/{ownTicket}")).StatusCode);

        var list = await client.GetStringAsync("/Tickets");
        Assert.DoesNotContain("Omar Haddad", list);
        Assert.Contains("Dana Whitfield", list);
    }

    [Fact]
    public async Task Requester_cannot_edit_tickets_or_use_agent_ai_tools()
    {
        const string dana = "dana@lakeview.example";
        var client = await LoginAsync(dana, RequesterPassword);
        var ownTicket = await TicketIdForRequesterAsync(dana, own: true);

        var edit = await client.GetAsync($"/Tickets/Edit/{ownTicket}");
        Assert.Equal(HttpStatusCode.Redirect, edit.StatusCode);
        Assert.Contains("/Account/AccessDenied", edit.Headers.Location!.ToString());

        var summary = await PostApiAsync(client, $"/Tickets/Details/{ownTicket}", $"/api/assistant/tickets/{ownTicket}/summary");
        Assert.Equal(HttpStatusCode.Forbidden, summary.StatusCode);
    }

    [Fact]
    public async Task Assistant_api_requires_antiforgery_token()
    {
        var client = await LoginAsync(AgentEmail, AgentPassword);

        var response = await client.PostAsync("/api/assistant/kb-answer", JsonContent.Create(new { question = "vpn disconnects" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Agent_gets_ticket_summary_and_it_is_audited()
    {
        var client = await LoginAsync(AgentEmail, AgentPassword);

        var response = await PostApiAsync(client, "/Tickets/Details/1", "/api/assistant/tickets/1/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("TicketSummary", json.RootElement.GetProperty("type").GetString());
        Assert.Equal("mock", json.RootElement.GetProperty("provider").GetString());
        var interactionId = json.RootElement.GetProperty("interactionId").GetInt64();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        var audit = await db.AiInteractions.SingleAsync(i => i.Id == interactionId);
        Assert.Equal(1, audit.TicketId);
        Assert.True(audit.Succeeded);
    }

    [Fact]
    public async Task Kb_answer_endpoint_validates_input_and_answers()
    {
        var client = await LoginAsync("omar@lakeview.example", RequesterPassword);

        var invalid = await PostApiAsync(client, "/Kb", "/api/assistant/kb-answer", new { question = "" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var ok = await PostApiAsync(client, "/Kb", "/api/assistant/kb-answer", new { question = "how do I reset my password" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using var json = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("found").GetBoolean());
        Assert.Equal("/kb/reset-your-network-password", json.RootElement.GetProperty("sources")[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task Requester_can_create_ticket_and_validation_errors_are_shown()
    {
        var client = await LoginAsync("lena@lakeview.example", RequesterPassword);
        var form = await client.GetStringAsync("/Tickets/Create");
        var token = ExtractToken(form);

        var invalid = await client.PostAsync("/Tickets/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Title"] = "x",
            ["Description"] = "short",
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("field-validation-error", await invalid.Content.ReadAsStringAsync());

        var created = await client.PostAsync("/Tickets/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Title"] = "Docking station not charging laptop",
            ["Description"] = "The dock powers the monitors but the laptop battery drains.",
            ["Category"] = "0",
            ["Priority"] = "1",
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Matches(@"/Tickets/Details/\d+", created.Headers.Location!.ToString());
    }
}
