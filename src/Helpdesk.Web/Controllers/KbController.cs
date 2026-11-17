using Helpdesk.Web.Data;
using Helpdesk.Web.Models;
using Helpdesk.Web.Services.Kb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Controllers;

[Authorize]
public class KbController : Controller
{
    private readonly HelpdeskDbContext _db;
    private readonly KbSearchService _search;

    public KbController(HelpdeskDbContext db, KbSearchService search)
    {
        _db = db;
        _search = search;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? q, TicketCategory? category, CancellationToken ct)
    {
        IReadOnlyList<KbArticle> articles;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var query = q.Length > 200 ? q[..200] : q;
            articles = (await _search.SearchAsync(query, top: 10, ct)).Select(h => h.Article).ToList();
        }
        else
        {
            articles = await _db.KbArticles.AsNoTracking().OrderBy(a => a.Title).ToListAsync(ct);
        }
        if (category is { } c)
        {
            articles = articles.Where(a => a.Category == c).ToList();
        }

        ViewData["Query"] = q;
        ViewData["Category"] = category;
        return View(articles);
    }

    [HttpGet]
    public async Task<IActionResult> Article(string slug, CancellationToken ct)
    {
        var article = await _db.KbArticles.AsNoTracking().FirstOrDefaultAsync(a => a.Slug == slug, ct);
        return article is null ? NotFound() : View(article);
    }
}
