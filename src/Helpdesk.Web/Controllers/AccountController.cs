using Helpdesk.Web.Models;
using Helpdesk.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Helpdesk.Web.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AccountController> _logger;

    public AccountController(SignInManager<ApplicationUser> signIn, IWebHostEnvironment env, ILogger<AccountController> logger)
    {
        _signIn = signIn;
        _env = env;
        _logger = logger;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ShowDemoHint"] = _env.IsDevelopment();
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        ViewData["ShowDemoHint"] = _env.IsDevelopment();
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _signIn.PasswordSignInAsync(model.Email, model.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            _logger.LogInformation("User {Email} signed in", model.Email);
            return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl) : RedirectToAction("Index", "Home");
        }

        ModelState.AddModelError(string.Empty, result.IsLockedOut
            ? "Account locked after too many failed attempts. Try again in a few minutes."
            : "Invalid email or password.");
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();
}
