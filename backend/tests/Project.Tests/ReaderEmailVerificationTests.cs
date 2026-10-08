using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Controllers;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

public sealed class ReaderEmailVerificationTests : IDisposable
{
    private const string ConfirmUrl = "https://localhost/ReaderRegistration/ConfirmEmail";

    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ApplicationDbContext db;
    private readonly FakeEmailSender emailSender = new();
    private readonly ReaderEmailVerificationService verification;
    private readonly ReaderRegistrationService registration;

    public ReaderEmailVerificationTests()
    {
        connection.Open();
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        registration = new ReaderRegistrationService(db, new PasswordHasher<ReaderAccount>(), NullLogger<ReaderRegistrationService>.Instance);
        verification = new ReaderEmailVerificationService(db, emailSender, registration, NullLogger<ReaderEmailVerificationService>.Instance);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    // ---------- Đăng ký gửi email xác nhận ----------

    [Fact]
    public async Task RegisteringCreatesUnconfirmedAccountAndSendsConfirmationEmail()
    {
        var result = await Controller().Register(NewRegistration());

        Assert.IsType<RedirectToActionResult>(result);
        var reader = await db.ReaderAccounts.AsNoTracking().SingleAsync();
        Assert.False(reader.EmailConfirmed);
        var sent = Assert.Single(emailSender.Sent);
        Assert.Equal("new.reader@example.com", sent.Recipient);
        Assert.Contains("ConfirmEmail?token=", sent.HtmlBody);
        var token = await db.ReaderEmailVerificationTokens.SingleAsync();
        Assert.Equal(TimeSpan.FromHours(24), token.ExpiresAtUtc - token.CreatedAtUtc);
        Assert.NotEqual(ExtractToken(sent.HtmlBody), token.TokenHash);
    }

    [Fact]
    public async Task RegisterSuccessPageTellsReaderToCheckEmail()
    {
        var controller = Controller();
        await controller.Register(NewRegistration());

        Assert.Equal("new.reader@example.com", controller.TempData["VerificationEmail"]);
        Assert.Equal(true, controller.TempData["VerificationEmailSent"]);
    }

    [Fact]
    public async Task RegistrationSucceedsEvenWhenEmailCannotBeSent()
    {
        emailSender.Fail = true;
        var controller = Controller();

        Assert.IsType<RedirectToActionResult>(await controller.Register(NewRegistration()));

        Assert.Single(await db.ReaderAccounts.ToListAsync());
        Assert.Equal(false, controller.TempData["VerificationEmailSent"]);
    }

    // ---------- Chưa xác nhận thì chưa đăng nhập được ----------

    [Fact]
    public async Task UnconfirmedReaderCannotSignInAndIsOfferedResend()
    {
        await Controller().Register(NewRegistration());
        var controller = Controller();

        var result = Assert.IsType<ViewResult>(await controller.Login("new.reader@example.com", "Password123"));

        Assert.Contains("chưa được xác nhận", controller.ModelState[string.Empty]!.Errors.Single().ErrorMessage);
        Assert.Equal("new.reader@example.com", (string)controller.ViewBag.UnconfirmedEmail);
        Assert.DoesNotContain(controller.Response.Headers.SetCookie, header => header!.StartsWith("reader_id="));
    }

    [Fact]
    public async Task WrongPasswordDoesNotRevealThatEmailIsUnconfirmed()
    {
        await Controller().Register(NewRegistration());
        var controller = Controller();

        await controller.Login("new.reader@example.com", "WrongPassword9");

        Assert.Equal("Email hoặc mật khẩu không chính xác.", controller.ModelState[string.Empty]!.Errors.Single().ErrorMessage);
        Assert.Null(controller.ViewBag.UnconfirmedEmail);
    }

    [Fact]
    public async Task ConfirmingEmailAllowsSignIn()
    {
        await Controller().Register(NewRegistration());

        var confirm = Assert.IsType<ViewResult>(await Controller().ConfirmEmail(ExtractToken(emailSender.Sent.Single().HtmlBody)));
        Assert.Equal(EmailConfirmationResult.Confirmed, (EmailConfirmationResult)confirm.ViewData["Result"]!);

        Assert.True((await db.ReaderAccounts.AsNoTracking().SingleAsync()).EmailConfirmed);
        var login = await Controller().Login("new.reader@example.com", "Password123");
        Assert.IsType<RedirectToActionResult>(login);
    }

    [Fact]
    public async Task ExistingReadersCreatedBeforeTheFeatureCanStillSignIn()
    {
        var legacy = new ReaderAccount
        {
            FullName = "Cũ", DateOfBirth = new DateOnly(2000, 1, 1), Email = "old@example.com",
            PhoneNumber = "0911111111", StudentOrStaffCode = "OLD-1", Status = "Đang hoạt động"
        };
        legacy.PasswordHash = new PasswordHasher<ReaderAccount>().HashPassword(legacy, "Password123");
        db.ReaderAccounts.Add(legacy);
        await db.SaveChangesAsync();

        Assert.IsType<RedirectToActionResult>(await Controller().Login("old@example.com", "Password123"));
    }

    // ---------- Liên kết dùng một lần, hết hạn sau 24 giờ ----------

    [Fact]
    public async Task UsedLinkReportsAlreadyConfirmed()
    {
        var reader = await AddUnconfirmedReaderAsync();
        await verification.SendAsync(reader, ConfirmUrl);
        var token = ExtractToken(emailSender.Sent.Single().HtmlBody);
        Assert.Equal(EmailConfirmationResult.Confirmed, (await verification.ConfirmAsync(token)).Result);

        Assert.Equal(EmailConfirmationResult.AlreadyConfirmed, (await verification.ConfirmAsync(token)).Result);
    }

    [Fact]
    public async Task ExpiredLinkIsRejected()
    {
        var reader = await AddUnconfirmedReaderAsync();
        await verification.SendAsync(reader, ConfirmUrl);
        await db.ReaderEmailVerificationTokens.ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        db.ChangeTracker.Clear(); // mỗi request thật dùng DbContext mới

        Assert.Equal(EmailConfirmationResult.InvalidOrExpired, (await verification.ConfirmAsync(ExtractToken(emailSender.Sent.Single().HtmlBody))).Result);
        Assert.False((await db.ReaderAccounts.AsNoTracking().SingleAsync()).EmailConfirmed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    public async Task UnknownLinkIsRejected(string token)
    {
        Assert.Equal(EmailConfirmationResult.InvalidOrExpired, (await verification.ConfirmAsync(token)).Result);
    }

    // ---------- Gửi lại email ----------

    [Fact]
    public async Task ResendingInvalidatesThePreviousLink()
    {
        var reader = await AddUnconfirmedReaderAsync();
        await verification.SendAsync(reader, ConfirmUrl);
        var oldToken = ExtractToken(emailSender.Sent[0].HtmlBody);

        db.ChangeTracker.Clear(); // mỗi request thật dùng DbContext mới
        Assert.NotNull(await verification.ResendAsync(" READER@example.com ", ConfirmUrl));
        db.ChangeTracker.Clear();
        var newToken = ExtractToken(emailSender.Sent[1].HtmlBody);

        Assert.Equal(EmailConfirmationResult.InvalidOrExpired, (await verification.ConfirmAsync(oldToken)).Result);
        Assert.Equal(EmailConfirmationResult.Confirmed, (await verification.ConfirmAsync(newToken)).Result);
    }

    [Fact]
    public async Task ResendIsLimitedToThreeEmailsPerHour()
    {
        var reader = await AddUnconfirmedReaderAsync();
        for (var i = 0; i < 3; i++) Assert.NotNull(await verification.ResendAsync("reader@example.com", ConfirmUrl));

        Assert.Null(await verification.ResendAsync("reader@example.com", ConfirmUrl));
        Assert.Equal(3, emailSender.Sent.Count);
    }

    [Fact]
    public async Task ResendSendsNothingForUnknownOrConfirmedEmails()
    {
        var reader = await AddUnconfirmedReaderAsync();
        reader.EmailConfirmed = true;
        await db.SaveChangesAsync();

        Assert.Null(await verification.ResendAsync("reader@example.com", ConfirmUrl));
        Assert.Null(await verification.ResendAsync("nobody@example.com", ConfirmUrl));
        Assert.Empty(emailSender.Sent);
    }

    [Fact]
    public async Task ResendScreenShowsTheSameMessageWhetherOrNotEmailExists()
    {
        await AddUnconfirmedReaderAsync();
        var known = Controller();
        var unknown = Controller();

        await known.ResendConfirmation(new ResendEmailConfirmationViewModel { Email = "reader@example.com" });
        await unknown.ResendConfirmation(new ResendEmailConfirmationViewModel { Email = "nobody@example.com" });

        Assert.Equal((string)known.ViewBag.Message, (string)unknown.ViewBag.Message);
    }

    // ---------- Không cần thủ thư duyệt: kích hoạt và cấp thẻ khi xác nhận email ----------

    [Fact]
    public async Task ConfirmingEmailActivatesAccountAndIssuesChosenCardForOneYear()
    {
        var (_, student) = await SeedCardTypesAsync();
        var model = NewRegistration();
        model.LibraryCardTypeId = student.Id;
        await Controller().Register(model);

        var outcome = await verification.ConfirmAsync(ExtractToken(emailSender.Sent.Single().HtmlBody));

        Assert.Equal(EmailConfirmationResult.Confirmed, outcome.Result);
        db.ChangeTracker.Clear();
        var reader = await db.ReaderAccounts.Include(item => item.LibraryCard).SingleAsync();
        Assert.Equal("Đang hoạt động", reader.Status);
        Assert.NotNull(reader.LibraryCard);
        Assert.Equal(student.Id, reader.LibraryCard.LibraryCardTypeId);
        Assert.Equal("Đang hoạt động", reader.LibraryCard.Status);
        var today = DateOnly.FromDateTime(DateTime.Today);
        Assert.Equal((today, today.AddYears(1)), (reader.LibraryCard.IssuedOn, reader.LibraryCard.ExpiresOn));
        Assert.Equal(reader.LibraryCard.CardCode, outcome.IssuedCard!.CardCode);
    }

    [Fact]
    public async Task ActivatedReaderCanPlaceAHoldWithoutLibrarianApproval()
    {
        var (regular, _) = await SeedCardTypesAsync();
        var model = NewRegistration();
        model.LibraryCardTypeId = regular.Id;
        await Controller().Register(model);
        await verification.ConfirmAsync(ExtractToken(emailSender.Sent.Single().HtmlBody));
        var author = new Author { Name = "Nam Cao" };
        db.Authors.Add(author);
        await db.SaveChangesAsync();
        var book = new Book { Title = "Chí Phèo", AuthorId = author.Id };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reader = await db.ReaderAccounts.AsNoTracking().SingleAsync();
        var hold = await registration.HoldDocumentAsync(reader.Id, book.Id);

        Assert.True(hold.IsAllowed, hold.Message);
    }

    [Fact]
    public async Task InactiveChosenCardTypeFallsBackToFirstActiveType()
    {
        var (regular, student) = await SeedCardTypesAsync();
        var model = NewRegistration();
        model.LibraryCardTypeId = student.Id;
        await Controller().Register(model);
        await db.LibraryCardTypes.Where(type => type.Id == student.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(type => type.IsActive, false));
        db.ChangeTracker.Clear();

        var outcome = await verification.ConfirmAsync(ExtractToken(emailSender.Sent.Single().HtmlBody));

        Assert.Equal(regular.Id, outcome.IssuedCard!.LibraryCardTypeId);
    }

    [Fact]
    public async Task ConfirmingDoesNotIssueASecondCardToAnAlreadyActiveReader()
    {
        await SeedCardTypesAsync();
        var reader = await AddUnconfirmedReaderAsync();
        reader.Status = "Đang hoạt động";
        await db.SaveChangesAsync();
        await verification.SendAsync(reader, ConfirmUrl);

        var outcome = await verification.ConfirmAsync(ExtractToken(emailSender.Sent.Single().HtmlBody));

        Assert.Equal(EmailConfirmationResult.Confirmed, outcome.Result);
        Assert.Null(outcome.IssuedCard);
        Assert.Empty(await db.LibraryCards.ToListAsync());
    }

    [Fact]
    public async Task AutomaticCardIssueIsRecordedInTheActivityLog()
    {
        await SeedCardTypesAsync();
        await Controller().Register(NewRegistration());

        await Controller().ConfirmEmail(ExtractToken(emailSender.Sent.Single().HtmlBody));

        var log = await db.AuditLogs.AsNoTracking().SingleAsync(item => item.Action == AuditActions.IssueCard);
        Assert.Equal("new.reader@example.com", log.Actor);
        Assert.Contains("tự động khi xác nhận email", log.Target);
    }

    [Fact]
    public async Task RegistrationRejectsAnInactiveCardType()
    {
        var (_, student) = await SeedCardTypesAsync();
        await db.LibraryCardTypes.Where(type => type.Id == student.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(type => type.IsActive, false));
        var model = NewRegistration();
        model.LibraryCardTypeId = student.Id;
        var controller = Controller();

        Assert.IsType<ViewResult>(await controller.Register(model));

        Assert.Equal("Loại thẻ không tồn tại hoặc đã ngừng sử dụng.",
            controller.ModelState[nameof(ReaderRegistrationViewModel.LibraryCardTypeId)]!.Errors.Single().ErrorMessage);
        Assert.Empty(await db.ReaderAccounts.ToListAsync());
    }

    // ---------- Đổi email ở trang hồ sơ phải xác nhận địa chỉ mới ----------

    [Fact]
    public async Task EmailChangeSendsLinkToNewAddressAndKeepsOldEmailUntilConfirmed()
    {
        var reader = await RequestEmailChangeAsync("moi@example.com");

        var result = await verification.SendEmailChangeAsync(reader, ConfirmUrl);

        Assert.True(result.Sent);
        var sent = Assert.Single(emailSender.Sent);
        Assert.Equal("moi@example.com", sent.Recipient);
        db.ChangeTracker.Clear();
        var stored = await db.ReaderAccounts.SingleAsync();
        Assert.Equal("cu@example.com", stored.Email);
        Assert.NotNull(await registration.AuthenticateReaderAsync("cu@example.com", "Password123"));
    }

    [Fact]
    public async Task ConfirmingEmailChangeSwitchesLoginEmail()
    {
        var reader = await RequestEmailChangeAsync("moi@example.com");
        await verification.SendEmailChangeAsync(reader, ConfirmUrl);

        var outcome = await verification.ConfirmAsync(ExtractToken(emailSender.Sent.Single().HtmlBody));

        Assert.Equal(EmailConfirmationResult.EmailChanged, outcome.Result);
        Assert.Equal("cu@example.com", outcome.PreviousEmail);
        db.ChangeTracker.Clear();
        var stored = await db.ReaderAccounts.SingleAsync();
        Assert.Equal("moi@example.com", stored.Email);
        Assert.Null(stored.PendingEmail);
        Assert.NotNull(await registration.AuthenticateReaderAsync("moi@example.com", "Password123"));
        Assert.Equal(EmailConfirmationResult.AlreadyConfirmed,
            (await verification.ConfirmAsync(ExtractToken(emailSender.Sent.Single().HtmlBody))).Result);
    }

    [Fact]
    public async Task NewerEmailChangeRequestInvalidatesTheOlderLink()
    {
        var reader = await RequestEmailChangeAsync("thu1@example.com");
        await verification.SendEmailChangeAsync(reader, ConfirmUrl);
        await registration.UpdateReaderContactAsync(reader.Id, reader.PhoneNumber, "", "thu2@example.com", "Password123");
        await verification.SendEmailChangeAsync(reader, ConfirmUrl);

        var first = await verification.ConfirmAsync(ExtractToken(emailSender.Sent[0].HtmlBody));

        Assert.Equal(EmailConfirmationResult.InvalidOrExpired, first.Result);
        db.ChangeTracker.Clear();
        Assert.Equal("cu@example.com", (await db.ReaderAccounts.SingleAsync()).Email);
    }

    [Fact]
    public async Task EmailChangeIsRejectedWhenAddressWasTakenWhileWaiting()
    {
        var reader = await RequestEmailChangeAsync("moi@example.com");
        await verification.SendEmailChangeAsync(reader, ConfirmUrl);
        db.ReaderAccounts.Add(new ReaderAccount
        {
            FullName = "Người khác", DateOfBirth = new DateOnly(2000, 1, 1), Email = "moi@example.com",
            PhoneNumber = "0911000111", StudentOrStaffCode = "R-9", Status = "Đang hoạt động", PasswordHash = "x"
        });
        await db.SaveChangesAsync();

        var outcome = await verification.ConfirmAsync(ExtractToken(emailSender.Sent.Single().HtmlBody));

        Assert.Equal(EmailConfirmationResult.EmailInUse, outcome.Result);
        db.ChangeTracker.Clear();
        Assert.Equal("cu@example.com", (await db.ReaderAccounts.SingleAsync(item => item.Id == reader.Id)).Email);
    }

    [Fact]
    public async Task EmailChangeRequestsAreLimitedToThreePerHour()
    {
        var reader = await RequestEmailChangeAsync("moi@example.com");
        for (var i = 0; i < 3; i++) Assert.True((await verification.SendEmailChangeAsync(reader, ConfirmUrl)).Sent);

        var fourth = await verification.SendEmailChangeAsync(reader, ConfirmUrl);

        Assert.True(fourth.Throttled);
        Assert.Equal(3, emailSender.Sent.Count);
    }

    private async Task<ReaderAccount> RequestEmailChangeAsync(string newEmail)
    {
        var hasher = new PasswordHasher<ReaderAccount>();
        var reader = new ReaderAccount
        {
            FullName = "Bạn đọc", DateOfBirth = new DateOnly(2000, 1, 1), Email = "cu@example.com",
            PhoneNumber = "0912345678", StudentOrStaffCode = "R-1", Status = "Đang hoạt động", EmailConfirmed = true
        };
        reader.PasswordHash = hasher.HashPassword(reader, "Password123");
        db.ReaderAccounts.Add(reader);
        await db.SaveChangesAsync();
        Assert.Equal(ReaderContactUpdateResult.EmailChangePending,
            await registration.UpdateReaderContactAsync(reader.Id, reader.PhoneNumber, "", newEmail, "Password123"));
        return reader;
    }

    private async Task<(LibraryCardType Regular, LibraryCardType Student)> SeedCardTypesAsync()
    {
        var regular = new LibraryCardType { Name = "Thẻ bạn đọc thường" };
        var student = new LibraryCardType { Name = "Thẻ sinh viên" };
        db.LibraryCardTypes.AddRange(regular, student);
        await db.SaveChangesAsync();
        return (regular, student);
    }

    private async Task<ReaderAccount> AddUnconfirmedReaderAsync()
    {
        var reader = new ReaderAccount
        {
            FullName = "Bạn đọc", DateOfBirth = new DateOnly(2000, 1, 1), Email = "reader@example.com",
            PhoneNumber = "0912345678", StudentOrStaffCode = "R-1", Status = "Chờ duyệt", EmailConfirmed = false,
            PasswordHash = "x"
        };
        db.ReaderAccounts.Add(reader);
        await db.SaveChangesAsync();
        return reader;
    }

    private static ReaderRegistrationViewModel NewRegistration() => new()
    {
        FullName = "Nguyen Van B",
        DateOfBirth = new DateOnly(2001, 2, 3),
        Email = "new.reader@example.com",
        PhoneNumber = "0911222333",
        StudentOrStaffCode = "SV-NEW",
        Password = "Password123",
        ConfirmPassword = "Password123",
        LibraryCardTypeId = null
    };

    private static string ExtractToken(string htmlBody) =>
        Uri.UnescapeDataString(WebUtility.HtmlDecode(Regex.Match(htmlBody, "token=([^\"&]+)").Groups[1].Value));

    private ReaderRegistrationController Controller()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");
        var controller = new ReaderRegistrationController(registration, new ReaderRegistrationIpRateLimiter(),
            new ReaderPasswordResetService(db, new PasswordHasher<ReaderAccount>(), emailSender, NullLogger<ReaderPasswordResetService>.Instance),
            new EphemeralDataProtectionProvider(), new AuditLogService(db, NullLogger<AuditLogService>.Instance), verification)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            Url = new FixedUrlHelper()
        };
        controller.TempData = new TempDataDictionary(context, new NoTempDataProvider());
        return controller;
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
        public string? Action(UrlActionContext actionContext) => $"https://localhost/ReaderRegistration/{actionContext.Action}";
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
