using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Controllers;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

/// <summary>Nhật ký phải ghi rõ đổi mật khẩu, đặt lại mật khẩu, đăng nhập thất bại, đăng xuất, mượn/gia hạn, đặt giữ/hủy giữ.</summary>
public sealed class ActivityLogTests : IDisposable
{
    private const string Password = "Password123";
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly ApplicationDbContext db;
    private readonly ReaderRegistrationService registration;
    private readonly EphemeralDataProtectionProvider protector = new();

    public ActivityLogTests()
    {
        connection.Open();
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        registration = new ReaderRegistrationService(db, new PasswordHasher<ReaderAccount>(), NullLogger<ReaderRegistrationService>.Instance);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    [Fact]
    public async Task ReaderPasswordChangeIsLoggedForSuccessAndFailure()
    {
        var reader = await AddReaderAsync();
        var controller = ReaderController(signedIn: reader);

        await controller.ChangePassword(new ReaderChangePasswordViewModel
            { CurrentPassword = "wrong-pass1", NewPassword = "NewPass123", ConfirmNewPassword = "NewPass123" });
        await controller.ChangePassword(new ReaderChangePasswordViewModel
            { CurrentPassword = Password, NewPassword = "NewPass123", ConfirmNewPassword = "NewPass123" });

        var logs = await Logs(AuditActions.ChangePassword);
        Assert.Equal(2, logs.Count);
        Assert.All(logs, log => Assert.Equal(reader.Email, log.Actor));
        Assert.Contains(logs, log => log.Target.Contains("thất bại: mật khẩu cũ không chính xác"));
        Assert.Contains(logs, log => log.Target.Contains("đổi mật khẩu thành công"));
        Assert.All(logs, log => Assert.Contains($"bạn đọc #{reader.Id} ", log.Target));
    }

    [Fact]
    public async Task PasswordResetThroughEmailIsLoggedWithTheAccount()
    {
        var reader = await AddReaderAsync();
        var emailSender = new CapturingEmailSender();
        var resetService = new ReaderPasswordResetService(db, new PasswordHasher<ReaderAccount>(), emailSender, NullLogger<ReaderPasswordResetService>.Instance);
        var controller = ReaderController(resetService: resetService);

        await controller.ForgotPassword(new ForgotPasswordViewModel { Email = reader.Email });
        var token = Uri.UnescapeDataString(WebUtility.HtmlDecode(
            System.Text.RegularExpressions.Regex.Match(emailSender.LastBody!, "token=([^\"&]+)").Groups[1].Value));
        await controller.ResetPassword(new ResetPasswordViewModel { Token = token, Password = "NewPass123", ConfirmPassword = "NewPass123" });

        var logs = await Logs(AuditActions.ResetPassword);
        Assert.Equal(2, logs.Count);
        Assert.Contains(logs, log => log.Target.Contains("Yêu cầu liên kết đặt lại mật khẩu"));
        Assert.Contains(logs, log => log.Target.Contains($"bạn đọc #{reader.Id} ") && log.Target.Contains("đặt lại mật khẩu qua email thành công"));
    }

    [Fact]
    public async Task FailedReaderLoginAndLogoutAreLogged()
    {
        var reader = await AddReaderAsync();

        await ReaderController().Login(reader.Email, "wrong-password");
        await ReaderController(signedIn: reader).Logout();

        var failed = Assert.Single(await Logs(AuditActions.LoginFailed));
        Assert.Equal(reader.Email, failed.Actor);
        Assert.Contains("sai email hoặc mật khẩu", failed.Target);
        var logout = Assert.Single(await Logs(AuditActions.Logout));
        Assert.Contains($"bạn đọc #{reader.Id} ", logout.Target);
    }

    [Fact]
    public async Task PlacingAndCancellingAHoldAreLoggedWithTheBookTitle()
    {
        await new WorkingScheduleService(db).GetWeeklySchedulesAsync();
        var reader = await AddReaderAsync(withCard: true);
        var book = new Book { Title = "Dế Mèn phiêu lưu ký", Author = new Author { Name = "Tô Hoài" } };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        var controller = ReaderController(signedIn: reader);

        await controller.HoldDocument(book.Id);
        var hold = await db.BookHolds.AsNoTracking().SingleAsync();
        await ReaderController(signedIn: reader).CancelMyHoldApi(hold.Id);

        var placed = Assert.Single(await Logs(AuditActions.PlaceHold));
        Assert.Contains("\"Dế Mèn phiêu lưu ký\"", placed.Target);
        Assert.Contains("xếp hàng chờ", placed.Target);
        var cancelled = Assert.Single(await Logs(AuditActions.CancelHold));
        Assert.Contains($"bạn đọc tự hủy đơn đặt giữ #{hold.Id}", cancelled.Target);
    }

    [Fact]
    public async Task CreatingAndRenewingALoanAreLoggedWithStaffBookAndReader()
    {
        await new WorkingScheduleService(db).GetWeeklySchedulesAsync();
        var reader = await AddReaderAsync(withCard: true);
        var book = new Book { Title = "Tắt đèn", Author = new Author { Name = "Ngô Tất Tố" } };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        var controller = LoanControllerFor("manager@library.test");

        await controller.Create(new CreateBookLoanViewModel { BookId = book.Id, ReaderAccountId = reader.Id, LoanDate = DateOnly.FromDateTime(DateTime.Today) });
        var loan = await db.BookLoans.AsNoTracking().SingleAsync();
        await controller.Renew(loan.Id);

        var created = Assert.Single(await Logs(AuditActions.CreateLoan));
        Assert.Equal("manager@library.test", created.Actor);
        Assert.Contains("\"Tắt đèn\"", created.Target);
        Assert.Contains($"bạn đọc #{reader.Id} ", created.Target);
        Assert.Contains($"lập phiếu mượn #{loan.Id}", created.Target);
        var renewed = Assert.Single(await Logs(AuditActions.RenewLoan));
        Assert.Contains($"gia hạn phiếu mượn #{loan.Id}", renewed.Target);
        Assert.Contains("lần gia hạn thứ 1", renewed.Target);
    }

    [Fact]
    public async Task KeywordFilterFindsEveryActivityOfOneReader()
    {
        var reader = await AddReaderAsync();
        db.AuditLogs.AddRange(
            AuditLogService.Create("staff@x.test", AuditActions.CreateLoan, $"Sách \"A\" (#1) – bạn đọc #{reader.Id} ({reader.Email}) – lập phiếu mượn", "1.1.1.1"),
            AuditLogService.Create(reader.Email, AuditActions.ChangePassword, $"Tài khoản bạn đọc #{reader.Id} ({reader.Email}) – đổi mật khẩu thành công", "1.1.1.1"),
            AuditLogService.Create("other@x.test", AuditActions.Login, "Tài khoản bạn đọc #999 (other@x.test)", "1.1.1.1"));
        await db.SaveChangesAsync();

        var logs = await new AuditLogService(db, NullLogger<AuditLogService>.Instance)
            .SearchAsync(new AuditLogFilter { Keyword = reader.Email }, 100);

        Assert.Equal(2, logs.Count);
        Assert.False(new AuditLogFilter { Keyword = "x" }.IsEmpty);
    }

    [Fact]
    public async Task AutomaticHoldExpiryIsLoggedAsSystem()
    {
        var reader = await AddReaderAsync();
        var book = new Book { Title = "Số đỏ", Author = new Author { Name = "Vũ Trọng Phụng" } };
        var copy = new BookCopy { Book = book, Shelf = new Shelf { Warehouse = new Warehouse { Code = "W", Name = "Kho" }, Code = "S", Name = "Kệ" }, CopyCode = "C-1", Status = BookCopyStatus.OnHold };
        db.BookCopies.Add(copy);
        await db.SaveChangesAsync();
        db.BookHolds.Add(new BookHold { BookId = book.Id, ReaderAccountId = reader.Id, Status = BookHoldStatus.Available, BookCopyId = copy.Id, PickupDeadlineUtc = DateTime.UtcNow.AddHours(-1) });
        await db.SaveChangesAsync();
        var fulfillment = new BookHoldFulfillmentService(db, new BookLoanService(db, new WorkingScheduleService(db)));

        await new HoldPickupExpiryService(db, fulfillment, NullLogger<HoldPickupExpiryService>.Instance).ExpireOverdueAsync(DateTime.UtcNow);

        var log = Assert.Single(await Logs(AuditActions.CancelHold));
        Assert.Equal("Hệ thống", log.Actor);
        Assert.Contains("\"Số đỏ\"", log.Target);
        Assert.Contains("quá hạn nhận sách", log.Target);
        Assert.Contains("C-1", log.Target);
    }

    private async Task<List<AuditLog>> Logs(string action) =>
        await db.AuditLogs.AsNoTracking().Where(log => log.Action == action).OrderBy(log => log.Id).ToListAsync();

    private async Task<ReaderAccount> AddReaderAsync(bool withCard = false)
    {
        var reader = new ReaderAccount
        {
            FullName = "Nguyễn Văn A", DateOfBirth = new DateOnly(2000, 1, 1), Email = "reader@example.com",
            PhoneNumber = "0912345678", StudentOrStaffCode = "SV-1", Status = "Đang hoạt động", EmailConfirmed = true
        };
        reader.PasswordHash = new PasswordHasher<ReaderAccount>().HashPassword(reader, Password);
        if (withCard)
        {
            reader.LibraryCard = new LibraryCard
            {
                CardCode = "LIB000001", Status = "Đang hoạt động",
                IssuedOn = DateOnly.FromDateTime(DateTime.Today), ExpiresOn = DateOnly.FromDateTime(DateTime.Today).AddYears(1),
                LibraryCardType = new LibraryCardType { Name = "Thẻ thường", MaxRenewals = 2 }
            };
        }
        db.ReaderAccounts.Add(reader);
        await db.SaveChangesAsync();
        return reader;
    }

    private ReaderRegistrationController ReaderController(ReaderAccount? signedIn = null, IReaderPasswordResetService? resetService = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");
        if (signedIn is not null)
        {
            var cookieContext = new DefaultHttpContext();
            ReaderSessionCookies.Append(cookieContext, protector, signedIn);
            context.Request.Headers.Cookie = string.Join("; ",
                cookieContext.Response.Headers.SetCookie.Select(header => header!.Split(';')[0]));
        }
        var controller = new ReaderRegistrationController(registration, new ReaderRegistrationIpRateLimiter(),
            resetService ?? new ReaderPasswordResetService(db, new PasswordHasher<ReaderAccount>(), new CapturingEmailSender(), NullLogger<ReaderPasswordResetService>.Instance),
            protector, new AuditLogService(db, NullLogger<AuditLogService>.Instance), new NoOpEmailVerificationService())
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            Url = new FixedUrlHelper()
        };
        controller.TempData = new TempDataDictionary(context, new NoTempDataProvider());
        return controller;
    }

    private LoanController LoanControllerFor(string staffEmail)
    {
        var services = new ServiceCollection()
            .AddSingleton<IAuditLogService>(new FixedStaffAuditLog(new AuditLogService(db, NullLogger<AuditLogService>.Instance), staffEmail))
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");
        var controller = new LoanController(new BookLoanService(db, new WorkingScheduleService(db)), db)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            Url = new FixedUrlHelper()
        };
        controller.TempData = new TempDataDictionary(context, new NoTempDataProvider());
        return controller;
    }

    private sealed class FixedStaffAuditLog(AuditLogService inner, string email) : IAuditLogService
    {
        public Task WriteAsync(string actor, string action, string target, string? ipAddress, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(actor, action, target, ipAddress, cancellationToken);
        public Task<IReadOnlyList<AuditLog>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) => inner.GetRecentAsync(limit, cancellationToken);
        public Task<IReadOnlyList<AuditLog>> SearchAsync(AuditLogFilter filter, int limit, CancellationToken cancellationToken = default) => inner.SearchAsync(filter, limit, cancellationToken);
        public Task<IReadOnlyList<string>> GetActorsAsync(CancellationToken cancellationToken = default) => inner.GetActorsAsync(cancellationToken);
        public Task<AdminAccount?> GetSignedInStaffAsync(HttpRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<AdminAccount?>(new AdminAccount { Id = 7, Email = email, Role = AccountRoles.LibraryManager, IsActive = true });
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public string? LastBody { get; private set; }
        public Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            LastBody = htmlBody;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext actionContext) => "https://localhost/" + actionContext.Action;
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
