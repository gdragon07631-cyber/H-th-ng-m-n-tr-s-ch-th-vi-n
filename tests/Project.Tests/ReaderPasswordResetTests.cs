using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Net.Http.Headers;
using Project.Controllers;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

public sealed class ReaderPasswordResetTests : IDisposable
{
    private const string ResetUrl = "https://localhost/ReaderRegistration/ResetPassword";
    private const string Email = "reader@example.com";

    // SQLite (not the InMemory provider) because the service uses ExecuteDeleteAsync.
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ApplicationDbContext db;
    private readonly PasswordHasher<ReaderAccount> hasher = new();
    private readonly FakeEmailSender emailSender = new();
    private readonly ReaderPasswordResetService service;

    public ReaderPasswordResetTests()
    {
        connection.Open();
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        service = new ReaderPasswordResetService(db, hasher, emailSender, NullLogger<ReaderPasswordResetService>.Instance);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    // ---------- Lát 1: liên kết 30 phút, dùng một lần ----------

    [Fact]
    public async Task RegisteredEmailReceivesLinkAndCanResetPasswordWithinThirtyMinutes()
    {
        await AddReaderAsync("old-password");

        Assert.True(await service.RequestAsync(Email, ResetUrl));

        var sent = Assert.Single(emailSender.Sent);
        Assert.Equal(Email, sent.Recipient);
        var token = ExtractToken(sent.HtmlBody);
        var stored = await db.ReaderPasswordResetTokens.SingleAsync();
        Assert.NotEqual(token, stored.TokenHash);
        Assert.InRange(stored.ExpiresAtUtc - stored.CreatedAtUtc, TimeSpan.FromMinutes(29.9), TimeSpan.FromMinutes(30.1));
        Assert.True(await service.IsTokenValidAsync(token));

        Assert.True(await service.ResetAsync(token, "new-password"));

        var registration = new ReaderRegistrationService(db, hasher, NullLogger<ReaderRegistrationService>.Instance);
        Assert.NotNull(await registration.AuthenticateReaderAsync(Email, "new-password"));
        Assert.Null(await registration.AuthenticateReaderAsync(Email, "old-password"));
        Assert.NotNull((await db.ReaderPasswordResetTokens.AsNoTracking().SingleAsync()).UsedAtUtc);
    }

    [Fact]
    public async Task UsedLinkCannotBeOpenedOrUsedAgain()
    {
        await AddReaderAsync("old-password");
        await service.RequestAsync(Email, ResetUrl);
        var token = ExtractToken(emailSender.Sent.Single().HtmlBody);
        Assert.True(await service.ResetAsync(token, "new-password"));

        Assert.False(await service.IsTokenValidAsync(token));
        Assert.False(await service.ResetAsync(token, "another-password"));
    }

    [Fact]
    public async Task LinkOlderThanThirtyMinutesIsRejected()
    {
        var reader = await AddReaderAsync("old-password");
        var originalHash = reader.PasswordHash;
        await service.RequestAsync(Email, ResetUrl);
        var token = ExtractToken(emailSender.Sent.Single().HtmlBody);
        var stored = await db.ReaderPasswordResetTokens.SingleAsync();
        stored.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-31);
        stored.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        Assert.False(await service.IsTokenValidAsync(token));
        Assert.False(await service.ResetAsync(token, "new-password"));
        Assert.Equal(originalHash, (await db.ReaderAccounts.AsNoTracking().SingleAsync()).PasswordHash);
    }

    [Fact]
    public void MismatchedNewPasswordFailsValidation()
    {
        var model = new ResetPasswordViewModel { Token = "t", Password = "new-password", ConfirmPassword = "other-password" };
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true));
        Assert.Contains(results, result => result.ErrorMessage == "Mật khẩu xác nhận không khớp.");
    }

    [Fact]
    public async Task InvalidTokenShowsInvalidLinkPage()
    {
        var controller = CreateController();

        var result = Assert.IsType<ViewResult>(await controller.ResetPassword("not-a-real-token"));

        Assert.True((bool)controller.ViewBag.InvalidToken);
        Assert.Equal(string.Empty, Assert.IsType<ResetPasswordViewModel>(result.Model).Token);
    }

    // ---------- Lát 2: không lộ email có tồn tại hay không ----------

    [Fact]
    public async Task RegisteredAndUnknownEmailsShowTheSameNeutralMessage()
    {
        await AddReaderAsync("old-password");

        var registeredController = CreateController();
        var registeredResult = Assert.IsType<ViewResult>(
            await registeredController.ForgotPassword(new ForgotPasswordViewModel { Email = Email }));
        var unknownController = CreateController();
        var unknownResult = Assert.IsType<ViewResult>(
            await unknownController.ForgotPassword(new ForgotPasswordViewModel { Email = "nobody@example.com" }));

        Assert.Equal((string)registeredController.ViewBag.Message, (string)unknownController.ViewBag.Message);
        Assert.Equal(registeredResult.ViewName, unknownResult.ViewName);
        Assert.True(registeredController.ModelState.IsValid);
        Assert.True(unknownController.ModelState.IsValid);
        Assert.Equal(Email, Assert.Single(emailSender.Sent).Recipient);
    }

    [Fact]
    public async Task SmtpFailureStillReturnsNeutralResult()
    {
        await AddReaderAsync("old-password");
        emailSender.Fail = true;

        Assert.True(await service.RequestAsync(Email, ResetUrl));
    }

    // ---------- Lát 3: tối đa 3 yêu cầu/giờ cho một email ----------

    [Fact]
    public async Task FourthRequestWithinAnHourIsRejected()
    {
        await AddReaderAsync("old-password");

        Assert.True(await service.RequestAsync(Email, ResetUrl));
        Assert.True(await service.RequestAsync(Email, ResetUrl));
        Assert.True(await service.RequestAsync(Email, ResetUrl));
        Assert.False(await service.RequestAsync(Email, ResetUrl));

        Assert.Equal(3, emailSender.Sent.Count);
    }

    [Fact]
    public async Task RateLimitIsPerEmailAndIgnoresCase()
    {
        await AddReaderAsync("old-password");
        for (var i = 0; i < 3; i++) await service.RequestAsync(Email, ResetUrl);

        Assert.False(await service.RequestAsync(" READER@example.com ", ResetUrl));
        Assert.True(await service.RequestAsync("other@example.com", ResetUrl));
    }

    [Fact]
    public async Task RequestsAreAllowedAgainAfterTheHourPasses()
    {
        await AddReaderAsync("old-password");
        for (var i = 0; i < 3; i++) await service.RequestAsync(Email, ResetUrl);
        Assert.False(await service.RequestAsync(Email, ResetUrl));

        await db.ReaderPasswordResetRequests.ExecuteUpdateAsync(setters =>
            setters.SetProperty(request => request.RequestedAtUtc, DateTime.UtcNow.AddHours(-1).AddMinutes(-1)));

        Assert.True(await service.RequestAsync(Email, ResetUrl));
    }

    [Fact]
    public async Task RateLimitedRequestShowsLimitMessage()
    {
        await AddReaderAsync("old-password");
        for (var i = 0; i < 3; i++) await service.RequestAsync(Email, ResetUrl);
        var controller = CreateController();

        await controller.ForgotPassword(new ForgotPasswordViewModel { Email = Email });

        Assert.Contains("quá nhiều yêu cầu", (string)controller.ViewBag.Message);
    }

    // ---------- Lát 4: đăng xuất các phiên cũ ----------

    [Fact]
    public async Task ResetSignsOutEveryOldSessionButKeepsNewLogin()
    {
        var reader = await AddReaderAsync("old-password");
        var protection = new EphemeralDataProtectionProvider();
        var firstSession = SignIn(protection, reader);
        var secondSession = SignIn(protection, reader);
        Assert.Equal(reader.Id, await GetReaderIdAsync(protection, firstSession));
        Assert.Equal(reader.Id, await GetReaderIdAsync(protection, secondSession));

        await service.RequestAsync(Email, ResetUrl);
        Assert.True(await service.ResetAsync(ExtractToken(emailSender.Sent.Single().HtmlBody), "new-password"));
        db.ChangeTracker.Clear();

        Assert.Equal(-1, await GetReaderIdAsync(protection, firstSession));
        Assert.Equal(-1, await GetReaderIdAsync(protection, secondSession));

        var refreshed = await db.ReaderAccounts.AsNoTracking().SingleAsync();
        var newSession = SignIn(protection, refreshed);
        Assert.Equal(reader.Id, await GetReaderIdAsync(protection, newSession));
    }

    [Fact]
    public async Task RejectedSessionClearsReaderCookies()
    {
        var reader = await AddReaderAsync("old-password");
        var protection = new EphemeralDataProtectionProvider();
        var oldSession = SignIn(protection, reader);
        reader.SessionVersion++;
        await db.SaveChangesAsync();

        var context = CreateRequest(oldSession);
        Assert.Equal(-1, await ReaderSessionCookies.GetReaderIdAsync(context, protection, FindReaderAsync, default));

        var cleared = ParseSetCookies(context)
            .Select(cookie => cookie.Name.Value).ToList();
        Assert.Contains("reader_id", cleared);
        Assert.Contains("reader_session_version", cleared);
    }

    private async Task<ReaderAccount> AddReaderAsync(string password)
    {
        var reader = new ReaderAccount
        {
            FullName = "Test Reader",
            DateOfBirth = new DateOnly(2000, 1, 1),
            Email = Email,
            PhoneNumber = "0912345678",
            StudentOrStaffCode = "R-1",
            Status = "Đang hoạt động"
        };
        reader.PasswordHash = hasher.HashPassword(reader, password);
        db.ReaderAccounts.Add(reader);
        await db.SaveChangesAsync();
        return reader;
    }

    private static string ExtractToken(string htmlBody) =>
        Uri.UnescapeDataString(Regex.Match(htmlBody, "token=([^\"&]+)").Groups[1].Value);

    private ReaderRegistrationController CreateController()
    {
        var registration = new ReaderRegistrationService(db, hasher, NullLogger<ReaderRegistrationService>.Instance);
        return new ReaderRegistrationController(registration, new ReaderRegistrationIpRateLimiter(), service,
            new EphemeralDataProtectionProvider(), new AuditLogService(db, NullLogger<AuditLogService>.Instance))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            Url = new FixedUrlHelper()
        };
    }

    private Task<ReaderAccount?> FindReaderAsync(int id, CancellationToken cancellationToken) =>
        db.ReaderAccounts.AsNoTracking().SingleOrDefaultAsync(reader => reader.Id == id, cancellationToken);

    private Task<int> GetReaderIdAsync(IDataProtectionProvider protection, string cookieHeader) =>
        ReaderSessionCookies.GetReaderIdAsync(CreateRequest(cookieHeader), protection, FindReaderAsync, default);

    /// <summary>Signs in on a fresh context and returns the browser's resulting Cookie header.</summary>
    private static string SignIn(IDataProtectionProvider protection, ReaderAccount reader)
    {
        var context = new DefaultHttpContext();
        ReaderSessionCookies.Append(context, protection, reader);
        return string.Join("; ", ParseSetCookies(context)
            .Select(cookie => $"{cookie.Name}={cookie.Value}"));
    }

    private static IList<SetCookieHeaderValue> ParseSetCookies(HttpContext context) =>
        SetCookieHeaderValue.ParseList(context.Response.Headers.SetCookie.Select(value => value!).ToList());

    private static DefaultHttpContext CreateRequest(string cookieHeader)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = cookieHeader;
        return context;
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

    private sealed class FixedUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext actionContext) => ResetUrl;
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => ResetUrl;
        public string? RouteUrl(UrlRouteContext routeContext) => ResetUrl;
    }
}
