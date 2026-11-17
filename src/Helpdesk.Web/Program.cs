using Helpdesk.Web.Data;
using Helpdesk.Web.Models;
using Helpdesk.Web.Services;
using Helpdesk.Web.Services.Ai;
using Helpdesk.Web.Services.Kb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHelpdeskDatabase(builder.Configuration);

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 10;
        o.User.RequireUniqueEmail = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<HelpdeskDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.Cookie.Name = "helpdesk.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.SlidingExpiration = true;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
});

// Every endpoint requires a signed-in user unless it opts out with [AllowAnonymous].
builder.Services.AddAuthorization(o =>
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// The assistant widget sends the antiforgery token in this header on its JSON POSTs.
builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");

builder.Services.AddControllersWithViews();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TicketService>();
builder.Services.AddScoped<KbSearchService>();
builder.Services.AddScoped<AssistantService>();
builder.Services.AddScoped<DataSeeder>();
builder.Services.AddLlmProviders(builder.Configuration);

var app = builder.Build();

await app.InitialiseDatabaseAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "kb-article",
    pattern: "kb/{slug}",
    defaults: new { controller = "Kb", action = "Article" });
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

public partial class Program
{
}
