using Microsoft.AspNetCore.Mvc;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

public sealed class LibrarianController(
    IAuthenticationService authenticationService,
    ITokenService tokenService) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        LoginViewModel model,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid)
        {
            ModelState.AddModelError(string.Empty, "Vui lòng kiểm tra email và mật khẩu.");
            return View(model);
        }

        var outcome = await authenticationService.LoginAsync(
            model.Email,
            model.Password,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken,
            AccountRoles.Librarian);

        if (outcome.Result == LoginResult.AccountLocked)
        {
            ModelState.AddModelError(string.Empty, "Tài khoản đang bị khóa. Vui lòng thử lại sau 15 phút.");
            return View(model);
        }

        if (outcome.Result != LoginResult.LoginSuccess || outcome.AdminAccountId is null)
        {
            ModelState.AddModelError(string.Empty, "Thông tin đăng nhập thủ thư không chính xác.");
            return View(model);
        }

        var tokenPair = await tokenService.CreateTokenPairAsync(outcome.AdminAccountId.Value, cancellationToken);
        Response.Cookies.Append("admin_refresh", tokenPair.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = tokenPair.RefreshTokenExpiresAtUtc,
            IsEssential = true
        });

        var destination = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Action("Index", "Book")!;
        return LocalRedirect(destination);
    }
}
