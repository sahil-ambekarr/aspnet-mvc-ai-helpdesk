using System.Diagnostics;
using Helpdesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Helpdesk.Web.Controllers;

public class HomeController : Controller
{
    private readonly TicketService _tickets;

    public HomeController(TicketService tickets) => _tickets = tickets;

    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await _tickets.GetDashboardAsync(User, ct));

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        ViewData["RequestId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        return View();
    }
}
