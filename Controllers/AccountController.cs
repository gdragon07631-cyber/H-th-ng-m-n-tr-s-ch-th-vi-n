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
        ViewData["HasRefreshToken"] = Request.Cookies.ContainsKey("admin_refresh");
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
            cancellationToken);
        if (outcome.Result == LoginResult.AccountLocked)
        {
            return StatusCode(StatusCodes.Status423Locked, new
            {
                message = "Tài khoản đang bị khóa. Vui lòng thử lại sau 15 phút."
            });
        }

        if (outcome.Result == LoginResult.LoginFailed || outcome.AdminAccountId is null)
        {
            return Unauthorized(new { message = "Email hoặc mật khẩu không chính xác." });
        }

        var tokenPair = await tokenService.CreateTokenPairAsync(outcome.AdminAccountId.Value, cancellationToken);
        SetRefreshTokenCookie(tokenPair);
        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            status = "LoginSuccess",
            tokenType = "Bearer",
            accessToken = tokenPair.AccessToken,
            accessTokenExpiresAtUtc = tokenPair.AccessTokenExpiresAtUtc,
            redirectUrl = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null
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
            Path = "/Account",
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
            Path = "/Account"
        });
    }
}
