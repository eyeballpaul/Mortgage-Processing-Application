using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PD.Mortgages.Staff.Web.Controllers;

[Route("account")]
public sealed class AccountController : Controller
{
    [AllowAnonymous]
    [HttpGet("sign-in")]
    public IActionResult SignIn(string? returnUrl = null)
    {
        var redirectUri = Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Content("~/");
        return Challenge(
            new AuthenticationProperties { RedirectUri = redirectUri },
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    [AllowAnonymous]
    [HttpPost("sign-out")]
    [ValidateAntiForgeryToken]
    public IActionResult SignOutUser() =>
        new SignOutResult(
            [
                CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme
            ],
            new AuthenticationProperties { RedirectUri = Url.Action(nameof(SignedOut)) ?? "/" });

    [AllowAnonymous]
    [HttpGet("signed-out")]
    public IActionResult SignedOut() => View();

    [AllowAnonymous]
    [HttpGet("access-denied")]
    public IActionResult AccessDenied() => View();
}
