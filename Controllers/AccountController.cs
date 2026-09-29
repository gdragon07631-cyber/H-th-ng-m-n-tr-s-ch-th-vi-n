using Microsoft.AspNetCore.Mvc;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

public sealed class AccountController(
    IAuthenticationService authenticationService,
    ITokenService tokenService) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        ViewData["HasRefreshToken"] = false;
        ViewData["DashboardUrl"] = Url.Action("Index", "Home");
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
            if (!IsAjaxRequest())
            {
                ViewData["HasRefreshToken"] = false;
                ViewData["DashboardUrl"] = Url.Action("Index", "Home");
                ModelState.AddModelError(string.Empty, "Vui lòng kiểm tra email và mật khẩu.");
                return View(model);
            }

            return BadRequest(new
            {
                message = "Vui lòng kiểm tra email và mật khẩu.",
                errors = ModelState.Where(item => item.Value?.Errors.Count > 0)
                    .ToDictionary(item => item.Key, item => item.Value!.Errors.Select(error => error.ErrorMessage))
            });
        }

        var outcome = await authenticationService.LoginAsync(
            model.Email,
            model.Password,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken,
            AccountRoles.SystemAdmin);
        if (outcome.Result == LoginResult.AccountLocked)
        {
            if (!IsAjaxRequest())
            {
                ViewData["HasRefreshToken"] = false;
                ViewData["DashboardUrl"] = Url.Action("Index", "Home");
                ModelState.AddModelError(string.Empty, "Tài khoản đang bị khóa. Vui lòng thử lại sau 15 phút.");
                return View(model);
            }

            return StatusCode(StatusCodes.Status423Locked, new
            {
                message = "Tài khoản đang bị khóa. Vui lòng thử lại sau 15 phút."
            });
        }

        if (outcome.Result == LoginResult.LoginFailed || outcome.AdminAccountId is null)
        {
            if (!IsAjaxRequest())
            {
                ViewData["HasRefreshToken"] = false;
                ViewData["DashboardUrl"] = Url.Action("Index", "Home");
                ModelState.AddModelError(string.Empty, "Email hoặc mật khẩu không chính xác.");
                return View(model);
            }

            return Unauthorized(new { message = "Email hoặc mật khẩu không chính xác." });
        }

        var tokenPair = await tokenService.CreateTokenPairAsync(outcome.AdminAccountId.Value, cancellationToken);
        SetRefreshTokenCookie(tokenPair);
        Response.Headers.CacheControl = "no-store";
        var destination = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Action("Index", "Home")!;
        if (!IsAjaxRequest()) return LocalRedirect(destination);

        return Ok(new
        {
            status = "LoginSuccess",
            tokenType = "Bearer",
            accessToken = tokenPair.AccessToken,
            accessTokenExpiresAtUtc = tokenPair.AccessTokenExpiresAtUtc,
            redirectUrl = destination
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Request.Cookies.TryGetValue("admin_refresh", out var refreshToken) ||
            string.IsNullOrWhiteSpace(refreshToken))
        {
            DeleteRefreshTokenCookie();
            return Unauthorized(new { message = "Phiên đăng nhập hết hạn. Vui lòng đăng nhập lại." });
        }

        var tokenPair = await tokenService.RefreshAsync(refreshToken, cancellationToken);
        if (tokenPair is null)
        {
            DeleteRefreshTokenCookie();
            return Unauthorized(new { message = "Phiên đăng nhập hết hạn. Vui lòng đăng nhập lại." });
        }

        SetRefreshTokenCookie(tokenPair);
        return Ok(new
        {
            status = "TokenRefreshed",
            tokenType = "Bearer",
            accessToken = tokenPair.AccessToken,
            accessTokenExpiresAtUtc = tokenPair.AccessTokenExpiresAtUtc
        });
    }

    /// <summary>Đăng xuất nhân sự: thu hồi phiên trong database rồi xoá cookie, đưa về đúng cổng đăng nhập.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken = default)
    {
        string? role = null;
        if (Request.Cookies.TryGetValue("admin_refresh", out var refreshToken))
            role = await tokenService.RevokeAsync(refreshToken, cancellationToken);
        DeleteRefreshTokenCookie();
        Response.Headers.CacheControl = "no-store";
        TempData["LogoutMessage"] = "Bạn đã đăng xuất.";

        var loginController = role switch
        {
            AccountRoles.Librarian => "Librarian",
            AccountRoles.LibraryManager => "Manager",
            _ => "Account"
        };
        return RedirectToAction(nameof(Login), loginController);
    }

    [HttpGet]
    public IActionResult LoginSuccess()
    {
        if (TempData["LoginStatus"] as string != "LoginSuccess")
        {
            return RedirectToAction(nameof(Login));
        }

        return View();
    }

    private void SetRefreshTokenCookie(TokenPair tokenPair)
    {
        Response.Cookies.Append("admin_refresh", tokenPair.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = tokenPair.RefreshTokenExpiresAtUtc,
            IsEssential = true
        });
    }

    private void DeleteRefreshTokenCookie()
    {
        Response.Cookies.Delete("admin_refresh", new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
    }

    private bool IsAjaxRequest() =>
        string.Equals(Request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
}
