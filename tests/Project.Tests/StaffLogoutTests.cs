using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Controllers;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

public sealed class StaffLogoutTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ApplicationDbContext db;
    private readonly TokenService tokenService;
    private readonly AuditLogService auditLogService;

    public StaffLogoutTests()
    {
        connection.Open();
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        tokenService = new TokenService(db, new ConfigurationBuilder().Build());
        auditLogService = new AuditLogService(db, NullLogger<AuditLogService>.Instance);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    [Theory]
    [InlineData(AccountRoles.SystemAdmin, "Account")]
    [InlineData(AccountRoles.LibraryManager, "Manager")]
    [InlineData(AccountRoles.Librarian, "Librarian")]
    public async Task LogoutRevokesSessionAndReturnsToTheRightLoginPage(string role, string expectedLoginController)
    {
        var token = await SignInAsync(role);
        var controller = CreateController($"admin_refresh={token}");

        var redirect = Assert.IsType<RedirectToActionResult>(await controller.Logout());

        Assert.Equal("Login", redirect.ActionName);
        Assert.Equal(expectedLoginController, redirect.ControllerName);
        Assert.NotNull((await db.RefreshTokens.AsNoTracking().SingleAsync()).RevokedAtUtc);
        Assert.Contains(controller.Response.Headers.SetCookie, header => header!.StartsWith("admin_refresh=;"));
        Assert.Equal("Bạn đã đăng xuất.", controller.TempData["LogoutMessage"]);
    }

    [Fact]
    public async Task OldCookieNoLongerWorksAfterLogout()
    {
        var token = await SignInAsync(AccountRoles.SystemAdmin);
        Assert.NotNull(await auditLogService.GetSignedInStaffAsync(Request($"admin_refresh={token}")));

        await CreateController($"admin_refresh={token}").Logout();

        Assert.Null(await auditLogService.GetSignedInStaffAsync(Request($"admin_refresh={token}")));
        Assert.Null(await tokenService.RefreshAsync(token));
    }

    [Fact]
    public async Task LogoutOnlyEndsTheCurrentSession()
    {
        var first = await SignInAsync(AccountRoles.SystemAdmin);
        var account = await db.AdminAccounts.SingleAsync();
        var second = await AddTokenAsync(account.Id);

        await CreateController($"admin_refresh={first}").Logout();

        Assert.Null(await auditLogService.GetSignedInStaffAsync(Request($"admin_refresh={first}")));
        Assert.NotNull(await auditLogService.GetSignedInStaffAsync(Request($"admin_refresh={second}")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("admin_refresh=unknown-token")]
    public async Task LogoutWithoutAValidSessionStillClearsCookieAndGoesToAdminLogin(string? cookie)
    {
        var controller = CreateController(cookie);

        var redirect = Assert.IsType<RedirectToActionResult>(await controller.Logout());

        Assert.Equal("Account", redirect.ControllerName);
        Assert.Contains(controller.Response.Headers.SetCookie, header => header!.StartsWith("admin_refresh=;"));
    }

    [Fact]
    public void LogoutRequiresPostWithAntiforgeryToken()
    {
        var method = typeof(AccountController).GetMethod(nameof(AccountController.Logout))!;

        Assert.NotNull(method.GetCustomAttributes(typeof(HttpPostAttribute), false).SingleOrDefault());
        Assert.NotNull(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), false).SingleOrDefault());
    }

    private async Task<string> SignInAsync(string role)
    {
        var account = new AdminAccount { Email = $"{role}@thuvien.vn", Role = role, IsActive = true };
        account.PasswordHash = new PasswordHasher<AdminAccount>().HashPassword(account, "MatKhau123");
        db.AdminAccounts.Add(account);
        await db.SaveChangesAsync();
        return await AddTokenAsync(account.Id);
    }

    private async Task<string> AddTokenAsync(int accountId)
    {
        var token = Guid.NewGuid().ToString("N");
        db.RefreshTokens.Add(new RefreshToken
        {
            AdminAccountId = accountId,
            TokenHash = TokenService.HashRefreshToken(token),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
        });
        await db.SaveChangesAsync();
        return token;
    }

    private static HttpRequest Request(string cookie)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = cookie;
        return context.Request;
    }

    private AccountController CreateController(string? cookie)
    {
        var context = new DefaultHttpContext();
        if (cookie is not null) context.Request.Headers.Cookie = cookie;
        var authentication = new AuthenticationService(db, new PasswordHasher<AdminAccount>(), NullLogger<AuthenticationService>.Instance);
        return new AccountController(authentication, tokenService)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            TempData = new TempDataDictionary(context, new NoTempDataProvider())
        };
    }

    private sealed class NoTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
