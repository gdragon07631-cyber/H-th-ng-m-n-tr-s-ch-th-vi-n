using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Controllers;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

public sealed class StaffAccountTests : IDisposable
{
    private const string SetupUrl = "https://localhost/StaffAccount/SetPassword";

    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ApplicationDbContext db;
    private readonly PasswordHasher<AdminAccount> hasher = new();
    private readonly FakeEmailSender emailSender = new();
    private readonly StaffAccountService service;
    private readonly AuditLogService auditLogService;

    public StaffAccountTests()
    {
        connection.Open();
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        service = new StaffAccountService(db, hasher, emailSender, NullLogger<StaffAccountService>.Instance);
        auditLogService = new AuditLogService(db, NullLogger<AuditLogService>.Instance);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    // ---------- Tạo tài khoản ----------

    [Fact]
    public async Task CreatingAccountStoresFormFieldsAndSendsTwentyFourHourSetupLink()
    {
        var result = await service.CreateAsync(Form("lan@thuvien.vn", AccountRoles.Librarian), SetupUrl);

        Assert.True(result.IsSuccess);
        Assert.True(result.EmailSent);
        var account = await db.AdminAccounts.AsNoTracking().SingleAsync();
        Assert.Equal("Nguyễn Thị Lan", account.FullName);
        Assert.Equal("lan@thuvien.vn", account.Email);
        Assert.Equal("0901234567", account.PhoneNumber);
        Assert.Equal(AccountRoles.Librarian, account.Role);
        Assert.True(account.IsActive);
        Assert.Equal(string.Empty, account.PasswordHash);

        var sent = Assert.Single(emailSender.Sent);
        Assert.Equal("lan@thuvien.vn", sent.Recipient);
        var token = await db.StaffPasswordSetupTokens.SingleAsync();
        Assert.Equal(TimeSpan.FromHours(24), token.ExpiresAtUtc - token.CreatedAtUtc);
        Assert.NotEqual(ExtractToken(sent.HtmlBody), token.TokenHash);
    }

    [Fact]
    public async Task CreatingAccountAsInactiveIsAllowed()
    {
        var model = Form("inactive@thuvien.vn", AccountRoles.LibraryManager);
        model.IsActive = false;

        await service.CreateAsync(model, SetupUrl);

        Assert.False((await db.AdminAccounts.SingleAsync()).IsActive);
    }

    [Theory]
    [InlineData("lan@thuvien.vn")]
    [InlineData("LAN@THUVIEN.VN")]
    [InlineData("  lan@thuvien.vn ")]
    public async Task DuplicateEmailIsRejectedWithTheEmailInTheMessage(string duplicate)
    {
        await service.CreateAsync(Form("lan@thuvien.vn", AccountRoles.Librarian), SetupUrl);

        var result = await service.CreateAsync(Form(duplicate, AccountRoles.LibraryManager), SetupUrl);

        Assert.Equal(StaffAccountStatus.DuplicateEmail, result.Status);
        Assert.Equal($"Email {duplicate.Trim()} đang được dùng bởi một tài khoản khác.", result.ErrorMessage);
        Assert.Single(await db.AdminAccounts.ToListAsync());
    }

    [Fact]
    public async Task DuplicateEmailOfExistingAdminIsRejectedOnEditToo()
    {
        await service.CreateAsync(Form("a@thuvien.vn", AccountRoles.Librarian), SetupUrl);
        var second = (await service.CreateAsync(Form("b@thuvien.vn", AccountRoles.Librarian), SetupUrl)).Account!;

        var result = await service.UpdateAsync(second.Id, Form("a@thuvien.vn", AccountRoles.Librarian), actingAdminId: 999);

        Assert.Equal(StaffAccountStatus.DuplicateEmail, result.Status);
        Assert.Contains("a@thuvien.vn", result.ErrorMessage);
    }

    // ---------- Đúng một trong ba vai trò ----------

    [Theory]
    [InlineData(AccountRoles.Librarian)]
    [InlineData(AccountRoles.LibraryManager)]
    [InlineData(AccountRoles.SystemAdmin)]
    public async Task EachOfTheThreeRolesCanBeAssigned(string role)
    {
        var result = await service.CreateAsync(Form($"{role}@thuvien.vn", role), SetupUrl);

        Assert.True(result.IsSuccess);
        Assert.Equal(role, (await db.AdminAccounts.SingleAsync()).Role);
    }

    [Theory]
    [InlineData("Reader")]
    [InlineData("")]
    [InlineData("Librarian,SystemAdmin")]
    public async Task AnyOtherRoleIsRejected(string role)
    {
        var result = await service.CreateAsync(Form("x@thuvien.vn", role), SetupUrl);

        Assert.Equal(StaffAccountStatus.InvalidRole, result.Status);
        Assert.Empty(await db.AdminAccounts.ToListAsync());
    }

    [Fact]
    public async Task DatabaseRejectsAnInvalidRole()
    {
        db.AdminAccounts.Add(new AdminAccount { Email = "bad@thuvien.vn", Role = "Superuser", PasswordHash = "x" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ---------- Đặt mật khẩu lần đầu ----------

    [Fact]
    public async Task NewAccountCannotSignInBeforeSettingPassword()
    {
        await service.CreateAsync(Form("lan@thuvien.vn", AccountRoles.Librarian), SetupUrl);

        var outcome = await Authentication().LoginAsync("lan@thuvien.vn", "", "127.0.0.1", default, AccountRoles.Librarian);

        Assert.Equal(LoginResult.LoginFailed, outcome.Result);
    }

    [Fact]
    public async Task SettingPasswordWithValidLinkAllowsSignInWithThatRole()
    {
        await service.CreateAsync(Form("quanly@thuvien.vn", AccountRoles.LibraryManager), SetupUrl);
        var token = ExtractToken(emailSender.Sent.Single().HtmlBody);

        Assert.True(await service.IsSetupTokenValidAsync(token));
        Assert.NotNull(await service.CompleteSetupAsync(token, "MatKhau123"));

        var auth = Authentication();
        Assert.Equal(LoginResult.LoginSuccess, (await auth.LoginAsync("quanly@thuvien.vn", "MatKhau123", "127.0.0.1", default, AccountRoles.LibraryManager)).Result);
        Assert.Equal(LoginResult.LoginFailed, (await auth.LoginAsync("quanly@thuvien.vn", "MatKhau123", "127.0.0.1", default, AccountRoles.SystemAdmin)).Result);
    }

    [Fact]
    public async Task SetupLinkCanBeUsedOnlyOnce()
    {
        await service.CreateAsync(Form("lan@thuvien.vn", AccountRoles.Librarian), SetupUrl);
        var token = ExtractToken(emailSender.Sent.Single().HtmlBody);
        await service.CompleteSetupAsync(token, "MatKhau123");

        Assert.False(await service.IsSetupTokenValidAsync(token));
        Assert.Null(await service.CompleteSetupAsync(token, "KhacMatKhau9"));
    }

    [Fact]
    public async Task SetupLinkExpiresAfterTwentyFourHours()
    {
        await service.CreateAsync(Form("lan@thuvien.vn", AccountRoles.Librarian), SetupUrl);
        var token = ExtractToken(emailSender.Sent.Single().HtmlBody);
        await db.StaffPasswordSetupTokens.ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.CreatedAtUtc, DateTime.UtcNow.AddHours(-24).AddMinutes(-1))
            .SetProperty(item => item.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));

        Assert.False(await service.IsSetupTokenValidAsync(token));
        Assert.Null(await service.CompleteSetupAsync(token, "MatKhau123"));
        Assert.Equal(string.Empty, (await db.AdminAccounts.AsNoTracking().SingleAsync()).PasswordHash);
    }

    [Fact]
    public async Task ResendingSetupEmailInvalidatesThePreviousLink()
    {
        var account = (await service.CreateAsync(Form("lan@thuvien.vn", AccountRoles.Librarian), SetupUrl)).Account!;
        var oldToken = ExtractToken(emailSender.Sent[0].HtmlBody);

        Assert.True((await service.ResendSetupEmailAsync(account.Id, SetupUrl)).IsSuccess);
        var newToken = ExtractToken(emailSender.Sent[1].HtmlBody);

        Assert.False(await service.IsSetupTokenValidAsync(oldToken));
        Assert.True(await service.IsSetupTokenValidAsync(newToken));
    }

    [Fact]
    public async Task ResendIsRefusedOnceAPasswordExists()
    {
        var account = (await service.CreateAsync(Form("lan@thuvien.vn", AccountRoles.Librarian), SetupUrl)).Account!;
        await service.CompleteSetupAsync(ExtractToken(emailSender.Sent[0].HtmlBody), "MatKhau123");

        Assert.Equal(StaffAccountStatus.PasswordAlreadySet, (await service.ResendSetupEmailAsync(account.Id, SetupUrl)).Status);
    }

    [Fact]
    public async Task AccountIsStillCreatedWhenEmailCannotBeSent()
    {
        emailSender.Fail = true;

        var result = await service.CreateAsync(Form("lan@thuvien.vn", AccountRoles.Librarian), SetupUrl);

        Assert.True(result.IsSuccess);
        Assert.False(result.EmailSent);
        Assert.StartsWith(SetupUrl + "?token=", result.SetupLink);
    }

    // ---------- Khoá tài khoản ----------

    [Fact]
    public async Task LockingRevokesCurrentSessionsImmediately()
    {
        var librarian = await AddActiveStaffAsync("lan@thuvien.vn", AccountRoles.Librarian, "MatKhau123");
        var cookie = await SignInAsync(librarian);
        Assert.NotNull(await auditLogService.GetSignedInStaffAsync(Request(cookie)));

        var result = await service.SetActiveAsync(librarian.Id, isActive: false, actingAdminId: 999);

        Assert.True(result.IsSuccess);
        Assert.Null(await auditLogService.GetSignedInStaffAsync(Request(cookie)));
        Assert.All(await db.RefreshTokens.AsNoTracking().ToListAsync(), token => Assert.NotNull(token.RevokedAtUtc));
        Assert.Equal(LoginResult.LoginFailed,
            (await Authentication().LoginAsync("lan@thuvien.vn", "MatKhau123", "127.0.0.1", default, AccountRoles.Librarian)).Result);
    }

    [Fact]
    public async Task DeactivatingThroughEditAlsoRevokesSessions()
    {
        var manager = await AddActiveStaffAsync("ql@thuvien.vn", AccountRoles.LibraryManager, "MatKhau123");
        var cookie = await SignInAsync(manager);
        var model = Form("ql@thuvien.vn", AccountRoles.LibraryManager);
        model.IsActive = false;

        Assert.True((await service.UpdateAsync(manager.Id, model, actingAdminId: 999)).IsSuccess);

        Assert.Null(await auditLogService.GetSignedInStaffAsync(Request(cookie)));
    }

    [Fact]
    public async Task UnlockingAllowsSigningInAgain()
    {
        var librarian = await AddActiveStaffAsync("lan@thuvien.vn", AccountRoles.Librarian, "MatKhau123");
        await service.SetActiveAsync(librarian.Id, false, 999);

        await service.SetActiveAsync(librarian.Id, true, 999);

        Assert.Equal(LoginResult.LoginSuccess,
            (await Authentication().LoginAsync("lan@thuvien.vn", "MatKhau123", "127.0.0.1", default, AccountRoles.Librarian)).Result);
    }

    [Fact]
    public async Task AdminCannotLockOrDemoteThemselves()
    {
        var admin = await AddActiveStaffAsync("admin@thuvien.vn", AccountRoles.SystemAdmin, "MatKhau123");

        Assert.Equal(StaffAccountStatus.CannotChangeOwnAccess, (await service.SetActiveAsync(admin.Id, false, admin.Id)).Status);
        Assert.Equal(StaffAccountStatus.CannotChangeOwnAccess,
            (await service.UpdateAsync(admin.Id, Form("admin@thuvien.vn", AccountRoles.Librarian), admin.Id)).Status);
        var reloaded = await db.AdminAccounts.AsNoTracking().SingleAsync();
        Assert.True(reloaded.IsActive);
        Assert.Equal(AccountRoles.SystemAdmin, reloaded.Role);
    }

    // ---------- Màn hình & phân quyền ----------

    [Fact]
    public async Task SystemAdminCreatesAccountThroughScreenAndActionIsLogged()
    {
        var admin = await AddActiveStaffAsync("admin@thuvien.vn", AccountRoles.SystemAdmin, "MatKhau123");
        var controller = Controller(await SignInAsync(admin));

        Assert.IsType<RedirectToActionResult>(await controller.Create(Form("lan@thuvien.vn", AccountRoles.Librarian)));

        var log = await db.AuditLogs.SingleAsync();
        Assert.Equal("admin@thuvien.vn", log.Actor);
        Assert.Equal(AuditActions.CreateAccount, log.Action);
        Assert.Contains("lan@thuvien.vn", log.Target);
    }

    [Fact]
    public async Task ScreenShowsDuplicateEmailErrorOnTheEmailField()
    {
        var admin = await AddActiveStaffAsync("admin@thuvien.vn", AccountRoles.SystemAdmin, "MatKhau123");
        var controller = Controller(await SignInAsync(admin));

        var view = Assert.IsType<ViewResult>(await controller.Create(Form("ADMIN@thuvien.vn", AccountRoles.Librarian)));

        Assert.Equal("Form", view.ViewName);
        Assert.Equal("Email ADMIN@thuvien.vn đang được dùng bởi một tài khoản khác.",
            controller.ModelState[nameof(StaffAccountFormViewModel.Email)]!.Errors.Single().ErrorMessage);
    }

    [Theory]
    [InlineData(AccountRoles.Librarian)]
    [InlineData(AccountRoles.LibraryManager)]
    public async Task OtherRolesCannotManageAccounts(string role)
    {
        var staff = await AddActiveStaffAsync($"{role}@thuvien.vn", role, "MatKhau123");
        var controller = Controller(await SignInAsync(staff));

        var result = Assert.IsType<ViewResult>(await controller.Create(Form("new@thuvien.vn", AccountRoles.SystemAdmin)));

        Assert.Equal("AccessDenied", result.ViewName);
        Assert.Equal(StatusCodes.Status403Forbidden, controller.Response.StatusCode);
        Assert.DoesNotContain(await db.AdminAccounts.ToListAsync(), account => account.Email == "new@thuvien.vn");
    }

    [Fact]
    public async Task AnonymousUserIsSentToAdminLogin()
    {
        var redirect = Assert.IsType<RedirectToActionResult>(await Controller(null).Index());
        Assert.Equal("Account", redirect.ControllerName);
    }

    [Fact]
    public async Task LibraryManagerSeesLibraryFunctionsButNotAdminOnes()
    {
        var manager = await AddActiveStaffAsync("ql@thuvien.vn", AccountRoles.LibraryManager, "MatKhau123");
        var cookie = await SignInAsync(manager);

        var home = new HomeController(db);
        Attach(home, cookie);
        Assert.IsType<ViewResult>(await home.Index());

        var policy = new LoanPolicyController(db, auditLogService);
        Attach(policy, cookie);
        Assert.IsType<ViewResult>(await policy.Index());

        var logs = new AuditLogController(auditLogService);
        Attach(logs, cookie);
        Assert.Equal("AccessDenied", Assert.IsType<ViewResult>(await logs.Index()).ViewName);
    }

    [Fact]
    public async Task LibrarianStillCannotOpenLibraryManagement()
    {
        var librarian = await AddActiveStaffAsync("lan@thuvien.vn", AccountRoles.Librarian, "MatKhau123");
        var home = new HomeController(db);
        Attach(home, await SignInAsync(librarian));

        Assert.IsType<RedirectToActionResult>(await home.Index());
    }

    private static StaffAccountFormViewModel Form(string email, string role) => new()
    {
        FullName = "Nguyễn Thị Lan",
        Email = email,
        PhoneNumber = "0901234567",
        Role = role,
        IsActive = true
    };

    private AuthenticationService Authentication() =>
        new(db, hasher, NullLogger<AuthenticationService>.Instance);

    private async Task<AdminAccount> AddActiveStaffAsync(string email, string role, string password)
    {
        var account = new AdminAccount { FullName = "Nhân sự", Email = email, Role = role, IsActive = true };
        account.PasswordHash = hasher.HashPassword(account, password);
        db.AdminAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    private async Task<string> SignInAsync(AdminAccount account)
    {
        var token = Guid.NewGuid().ToString("N");
        db.RefreshTokens.Add(new RefreshToken
        {
            AdminAccountId = account.Id,
            TokenHash = TokenService.HashRefreshToken(token),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();
        return $"admin_refresh={token}";
    }

    private static HttpRequest Request(string cookie)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = cookie;
        return context.Request;
    }

    private static string ExtractToken(string htmlBody) =>
        Uri.UnescapeDataString(WebUtility.HtmlDecode(Regex.Match(htmlBody, "token=([^\"&]+)").Groups[1].Value));

    private StaffAccountController Controller(string? cookie)
    {
        var controller = new StaffAccountController(new StaffAccountService(db, hasher, emailSender, NullLogger<StaffAccountService>.Instance),
            auditLogService, new TestEnvironment());
        Attach(controller, cookie);
        return controller;
    }

    private static void Attach(Controller controller, string? cookie)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
        if (cookie is not null) context.Request.Headers.Cookie = cookie;
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        controller.TempData = new TempDataDictionary(context, new NoTempDataProvider());
        controller.Url = new FixedUrlHelper();
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<(string Recipient, string Subject, string HtmlBody)> Sent { get; } = [];
        public bool Fail { get; set; }

        public Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("SMTP unavailable");
            Sent.Add((recipient, subject, htmlBody));
            return Task.CompletedTask;
        }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Project";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
    }

    private sealed class FixedUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext actionContext) => SetupUrl;
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => "/";
        public string? RouteUrl(UrlRouteContext routeContext) => "/";
    }

    private sealed class NoTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
