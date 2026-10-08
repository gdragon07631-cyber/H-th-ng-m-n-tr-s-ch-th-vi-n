using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Controllers;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

public sealed class AuditLogProtectionTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ApplicationDbContext db;
    private readonly AuditLogService service;

    public AuditLogProtectionTests()
    {
        connection.Open();
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        service = new AuditLogService(db, NullLogger<AuditLogService>.Instance);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    // ---------- Phân quyền ----------

    [Fact]
    public async Task SystemAdminCanViewAndFilterLogs()
    {
        await service.WriteAsync("a@example.com", AuditActions.Login, "Tài khoản A", "127.0.0.1");
        await service.WriteAsync("b@example.com", AuditActions.IssueCard, "Thẻ B", "127.0.0.1");
        var controller = CreateController(await SignInStaffAsync(AccountRoles.SystemAdmin));

        var all = Assert.IsType<AuditLogIndexViewModel>(Assert.IsType<ViewResult>(await controller.Index()).Model);
        var filtered = Assert.IsType<AuditLogIndexViewModel>(Assert.IsType<ViewResult>(
            await CreateController(await SignInStaffAsync(AccountRoles.SystemAdmin))
                .Index(new AuditLogFilter { Action = AuditActions.IssueCard })).Model);

        Assert.Equal(2, all.Logs.Count);
        Assert.Equal("b@example.com", Assert.Single(filtered.Logs).Actor);
    }

    [Theory]
    [InlineData(AccountRoles.Librarian)]
    [InlineData(AccountRoles.LibraryManager)]
    public async Task OtherStaffRolesAreForbidden(string role)
    {
        await service.WriteAsync("a@example.com", AuditActions.Login, "Tài khoản A", "127.0.0.1");
        var controller = CreateController(await SignInStaffAsync(role));

        var result = Assert.IsType<ViewResult>(await controller.Index(new AuditLogFilter { Actor = "a@example.com" }));

        Assert.Equal("AccessDenied", result.ViewName);
        Assert.Equal(StatusCodes.Status403Forbidden, controller.Response.StatusCode);
        Assert.Null(result.Model); // không lộ dữ liệu nhật ký
    }

    [Fact]
    public async Task AnonymousUserIsSentToAdminLogin()
    {
        var redirect = Assert.IsType<RedirectToActionResult>(await CreateController(cookieHeader: null).Index());

        Assert.Equal("Account", redirect.ControllerName);
        Assert.Equal("Login", redirect.ActionName);
    }

    [Fact]
    public async Task ReaderSessionCannotOpenLogs()
    {
        var redirect = Assert.IsType<RedirectToActionResult>(
            await CreateController("reader_id=1; reader_session_version=anything").Index());

        Assert.Equal("Account", redirect.ControllerName);
    }

    [Fact]
    public async Task DeactivatedAdminCannotOpenLogs()
    {
        var cookie = await SignInStaffAsync(AccountRoles.SystemAdmin, isActive: false);

        Assert.IsType<RedirectToActionResult>(await CreateController(cookie).Index());
    }

    [Fact]
    public async Task RevokedOrForgedAdminTokenCannotOpenLogs()
    {
        var cookie = await SignInStaffAsync(AccountRoles.SystemAdmin);
        await db.RefreshTokens.ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAtUtc, DateTime.UtcNow));

        Assert.IsType<RedirectToActionResult>(await CreateController(cookie).Index());
        Assert.IsType<RedirectToActionResult>(await CreateController("admin_refresh=forged-token").Index());
    }

    [Fact]
    public void LogPagesAreNeverCached()
    {
        var cache = typeof(AuditLogController).GetMethod(nameof(AuditLogController.Index))!
            .GetCustomAttribute<ResponseCacheAttribute>();

        Assert.NotNull(cache);
        Assert.True(cache.NoStore);
    }

    // ---------- Chỉ đọc ----------

    [Fact]
    public void ControllerOnlyExposesReadOnlyIndex()
    {
        var actions = typeof(AuditLogController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        var action = Assert.Single(actions);
        Assert.Equal(nameof(AuditLogController.Index), action.Name);
        Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
    }

    [Fact]
    public void ServiceHasNoUpdateOrDeleteOperation()
    {
        var names = typeof(IAuditLogService).GetMethods().Select(method => method.Name).ToList();

        Assert.DoesNotContain(names, name => Regex.IsMatch(name, "Update|Delete|Remove|Edit|Clear", RegexOptions.IgnoreCase));
    }

    [Fact]
    public void LogScreenHasNoEditOrDeleteControls()
    {
        var view = File.ReadAllText(FindRepoFile("frontend", "Views", "AuditLog", "Index.cshtml"));

        Assert.DoesNotMatch(new Regex("method=\"post\"", RegexOptions.IgnoreCase), view);
        Assert.DoesNotMatch(new Regex(@"asp-action=""(Edit|Delete|Update|Remove)""", RegexOptions.IgnoreCase), view);
        Assert.DoesNotMatch(new Regex(@">\s*(Sửa|Xóa)\s*<"), view);
        Assert.DoesNotMatch(new Regex("Xóa(?! bộ lọc)"), view); // chữ "Xóa" chỉ dùng cho "Xóa bộ lọc"
        Assert.DoesNotMatch(new Regex("Sửa"), view);
        Assert.Contains("Xóa bộ lọc", view);
    }

    [Fact]
    public async Task ModifyingALogIsRejected()
    {
        await service.WriteAsync("a@example.com", AuditActions.Login, "Tài khoản A", "127.0.0.1");
        var log = await db.AuditLogs.SingleAsync();

        log.Actor = "hacker@example.com";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        Assert.Contains("chỉ đọc", error.Message);
        Assert.Equal("a@example.com", (await db.AuditLogs.AsNoTracking().SingleAsync()).Actor);
    }

    [Fact]
    public async Task DeletingALogIsRejected()
    {
        await service.WriteAsync("a@example.com", AuditActions.Login, "Tài khoản A", "127.0.0.1");

        db.AuditLogs.Remove(await db.AuditLogs.SingleAsync());
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();

        Assert.Single(await db.AuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task NewActivityKeepsExistingLogsUnchanged()
    {
        await service.WriteAsync("a@example.com", AuditActions.Login, "Tài khoản A", "10.0.0.1");
        await service.WriteAsync("b@example.com", AuditActions.IssueCard, "Thẻ B", "10.0.0.2");
        var before = await SnapshotAsync();

        var admin = new AdminAccount { Email = "admin@example.com", Role = AccountRoles.SystemAdmin, IsActive = true };
        admin.PasswordHash = new PasswordHasher<AdminAccount>().HashPassword(admin, "admin-password");
        db.AdminAccounts.Add(admin);
        await db.SaveChangesAsync();
        var authentication = new AuthenticationService(db, new PasswordHasher<AdminAccount>(), NullLogger<AuthenticationService>.Instance);
        for (var i = 0; i < 3; i++)
            await authentication.LoginAsync("admin@example.com", "admin-password", "10.0.0.9", default, AccountRoles.SystemAdmin);
        await service.WriteAsync("c@example.com", AuditActions.UpdateLoanPolicy, "Chính sách mượn", "10.0.0.3");

        var after = await SnapshotAsync();
        Assert.Equal(before.Count + 4, after.Count);
        Assert.All(before, original => Assert.Contains(original, after));
    }

    private async Task<List<string>> SnapshotAsync() =>
        (await db.AuditLogs.AsNoTracking().OrderBy(log => log.Id).ToListAsync())
        .Select(log => $"{log.Id}|{log.OccurredAtUtc:O}|{log.Actor}|{log.Action}|{log.Target}|{log.IpAddress}")
        .ToList();

    private async Task<string> SignInStaffAsync(string role, bool isActive = true)
    {
        var account = new AdminAccount
        {
            Email = $"{role}-{Guid.NewGuid():N}@example.com",
            Role = role,
            IsActive = isActive,
            PasswordHash = "x"
        };
        var token = Guid.NewGuid().ToString("N");
        db.RefreshTokens.Add(new RefreshToken
        {
            AdminAccount = account,
            TokenHash = TokenService.HashRefreshToken(token),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();
        return $"admin_refresh={token}";
    }

    private AuditLogController CreateController(string? cookieHeader)
    {
        var context = new DefaultHttpContext();
        if (cookieHeader is not null) context.Request.Headers.Cookie = cookieHeader;
        return new AuditLogController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            Url = new FixedUrlHelper()
        };
    }

    private static string FindRepoFile(params string[] relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(Path.Combine(relativePath));
    }

    private sealed class FixedUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext actionContext) => "/" + actionContext.Action;
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => "/";
        public string? RouteUrl(UrlRouteContext routeContext) => "/";
    }
}
