using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Controllers;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

public sealed class ReaderRegistrationRateLimitTests
{
    [Fact]
    public void ThreeAttemptsAllowedFourthDeniedOtherIpAllowed()
    {
        var limiter = new ReaderRegistrationIpRateLimiter();
        for (var i = 0; i < 3; i++) Assert.True(Attempt(limiter, "192.168.1.10"));
        Assert.False(Attempt(limiter, "192.168.1.10"));
        Assert.True(Attempt(limiter, "192.168.1.20"));
    }

    [Fact]
    public void SlidingWindowExpiresOldestAttemptWithoutExtendingOnDenial()
    {
        var clock = new TestClock();
        var limiter = new ReaderRegistrationIpRateLimiter(clock);
        Assert.True(Attempt(limiter, "192.168.1.10")); // 10:15
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True(Attempt(limiter, "192.168.1.10")); // 10:25
        clock.Advance(TimeSpan.FromMinutes(25));
        Assert.True(Attempt(limiter, "192.168.1.10")); // 10:50
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.False(Attempt(limiter, "192.168.1.10")); // 11:00
        clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromTicks(1));
        Assert.False(Attempt(limiter, "192.168.1.10"));
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.True(Attempt(limiter, "192.168.1.10")); // 11:15
        Assert.False(Attempt(limiter, "192.168.1.10"));
        clock.Advance(TimeSpan.FromHours(1));
        Assert.True(Attempt(limiter, "192.168.1.10"));
    }

    [Theory]
    [InlineData("192.168.1.10", "::ffff:192.168.1.10")]
    [InlineData("127.0.0.1", "::1")]
    [InlineData("2001:db8::1", "2001:0db8:0:0:0:0:0:1")]
    public void EquivalentAddressesShareLimit(string first, string equivalent)
    {
        var limiter = new ReaderRegistrationIpRateLimiter();
        Assert.True(Attempt(limiter, first));
        Assert.True(Attempt(limiter, equivalent));
        Assert.True(Attempt(limiter, first));
        Assert.False(Attempt(limiter, equivalent));
    }

    [Fact]
    public async Task ConcurrentRequestsCannotExceedThree()
    {
        var limiter = new ReaderRegistrationIpRateLimiter();
        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ =>
            Task.Run(() => Attempt(limiter, "192.168.1.10"))));
        Assert.Equal(3, results.Count(allowed => allowed));
    }

    [Fact]
    public async Task DuplicatesAndSuccessCountAndFourthPreservesFormWithoutCreatingAccount()
    {
        using var db = CreateDb();
        var service = Service(db);
        await service.RegisterAsync(Model("old@example.com", "OLD001"));
        var limiter = new ReaderRegistrationIpRateLimiter();
        var first = Controller(service, limiter);
        Assert.IsType<ViewResult>(await first.Register(Model("old@example.com", "NEW001")));
        Assert.True((bool)first.ViewBag.ShowForgotPasswordSuggestion);
        var second = Controller(service, limiter);
        Assert.IsType<ViewResult>(await second.Register(Model("new@example.com", "OLD001")));
        Assert.True((bool)second.ViewBag.ShowCodeForgotPasswordSuggestion);
        Assert.IsType<RedirectToActionResult>(await Controller(service, limiter).Register(Model("third@example.com", "NEW003")));
        var blocked = Controller(service, limiter);
        blocked.Request.Headers["X-Forwarded-For"] = "192.168.1.20";
        blocked.Request.Headers["X-Real-IP"] = "192.168.1.30";
        var model = Model("fourth@example.com", "NEW004");
        Assert.Same(model, Assert.IsType<ViewResult>(await blocked.Register(model)).Model);
        Assert.Equal("Bạn đã gửi quá nhiều yêu cầu đăng ký. Vui lòng thử lại sau.", blocked.ModelState[string.Empty]!.Errors.Single().ErrorMessage);
        Assert.Equal(2, await db.ReaderAccounts.CountAsync());
        Assert.Empty(await db.LibraryCards.ToListAsync());
        Assert.Empty(await db.BookHolds.ToListAsync());
        Assert.IsType<RedirectToActionResult>(await Controller(service, limiter, "192.168.1.20").Register(model));
        Assert.Equal(3, await db.ReaderAccounts.CountAsync());
    }

    [Fact]
    public async Task GetsDoNotCountButInvalidPostsDo()
    {
        using var db = CreateDb();
        var service = Service(db);
        var limiter = new ReaderRegistrationIpRateLimiter();
        for (var i = 0; i < 5; i++) Assert.IsType<ViewResult>(Controller(service, limiter).Register());
        for (var i = 0; i < 3; i++)
        {
            var controller = Controller(service, limiter);
            controller.ModelState.AddModelError("Email", "Email không đúng định dạng.");
            Assert.IsType<ViewResult>(await controller.Register(Model("invalid", "NEW001")));
            Assert.False(controller.ModelState.ContainsKey(string.Empty));
        }
        var fourth = Controller(service, limiter);
        Assert.IsType<ViewResult>(await fourth.Register(Model("valid@example.com", "NEW004")));
        Assert.True(fourth.ModelState.ContainsKey(string.Empty));
        Assert.Empty(await db.ReaderAccounts.ToListAsync());
    }

    private static bool Attempt(ReaderRegistrationIpRateLimiter limiter, string ip)
    {
        if (!limiter.TryAcquire(ip, out var lease)) return false;
        using (lease) lease.Commit();
        return true;
    }
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 9, 28, 10, 15, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan interval) => now += interval;
    }
    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static ReaderRegistrationService Service(ApplicationDbContext db) => new(db, new PasswordHasher<ReaderAccount>(), NullLogger<ReaderRegistrationService>.Instance);
    private static ReaderRegistrationViewModel Model(string email, string code) => new()
    {
        FullName = "Test Reader", DateOfBirth = new DateOnly(2000, 1, 1), Email = email,
        PhoneNumber = "0912345678", StudentOrStaffCode = code, Password = "Abc12345", ConfirmPassword = "Abc12345"
    };
    private static ReaderRegistrationController Controller(IReaderRegistrationService service, ReaderRegistrationIpRateLimiter limiter, string ip = "192.168.1.10")
    {
        var controller = new ReaderRegistrationController(service, limiter);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        controller.TempData = new TempDataDictionary(controller.HttpContext, new TempDataProvider());
        return controller;
    }
    private sealed class TempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
