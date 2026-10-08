using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Controllers;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

public sealed class ReaderRegistrationTests
{
    private static ReaderRegistrationViewModel Valid() => new()
    {
        FullName = "  Nguyen Van A  ", DateOfBirth = new DateOnly(2000, 1, 1),
        Email = "student@example.com", PhoneNumber = "0912345678",
        StudentOrStaffCode = "  SV001  ", Password = "Abc12345", ConfirmPassword = "Abc12345",
        LibraryCardTypeId = 1
    };

    private static List<ValidationResult> Errors(ReaderRegistrationViewModel model)
    {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), errors, true);
        return errors;
    }

    [Fact]
    public void ValidRegistrationPasses() => Assert.Empty(Errors(Valid()));

    [Theory]
    [InlineData("FullName")]
    [InlineData("DateOfBirth")]
    [InlineData("Email")]
    [InlineData("PhoneNumber")]
    [InlineData("StudentOrStaffCode")]
    [InlineData("Password")]
    [InlineData("ConfirmPassword")]
    public void EveryFieldIsRequired(string field)
    {
        var model = Valid();
        typeof(ReaderRegistrationViewModel).GetProperty(field)!.SetValue(model, null);
        Assert.Contains(Errors(model), error => error.MemberNames.Contains(field));
    }

    [Theory]
    [InlineData("FullName", "   ")]
    [InlineData("StudentOrStaffCode", "   ")]
    [InlineData("Email", "abcgmail.com")]
    [InlineData("PhoneNumber", "abcdefghij")]
    [InlineData("PhoneNumber", "1912345678")]
    [InlineData("PhoneNumber", "091234567")]
    [InlineData("PhoneNumber", "09123456789")]
    [InlineData("Password", "Abc123")]
    [InlineData("Password", "abcdefgh")]
    [InlineData("Password", "12345678")]
    [InlineData("ConfirmPassword", "Abc123456")]
    public void InvalidInputHasFieldError(string field, string value)
    {
        var model = Valid();
        typeof(ReaderRegistrationViewModel).GetProperty(field)!.SetValue(model, value);
        Assert.Contains(Errors(model), error => error.MemberNames.Contains(field));
    }

    [Fact]
    public void FutureBirthDateIsRejectedButTodayIsAccepted()
    {
        var model = Valid();
        model.DateOfBirth = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        Assert.Contains(Errors(model), error => error.MemberNames.Contains("DateOfBirth"));
        model.DateOfBirth = DateOnly.FromDateTime(DateTime.Today);
        Assert.Empty(Errors(model));
    }

    [Fact]
    public async Task RegistrationPersistsPendingProfileWithHashedPasswordAndSuccessInstructions()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var hasher = new PasswordHasher<ReaderAccount>();
        var service = new ReaderRegistrationService(db, hasher, NullLogger<ReaderRegistrationService>.Instance);
        // Loại thẻ được chọn khi đăng ký phải tồn tại (database thật có sẵn từ migration).
        db.LibraryCardTypes.Add(new LibraryCardType { Id = 1, Name = "Thẻ bạn đọc thường" });
        await db.SaveChangesAsync();
        var controller = CreateController(service);
        var model = Valid();
        var result = Assert.IsType<RedirectToActionResult>(await controller.Register(model));
        Assert.Equal("RegisterSuccess", result.ActionName);
        db.ChangeTracker.Clear();
        var account = await db.ReaderAccounts.SingleAsync();
        Assert.Equal("Chờ duyệt", account.Status);
        Assert.Equal("Nguyen Van A", account.FullName);
        Assert.Equal("SV001", account.StudentOrStaffCode);
        Assert.NotEqual(model.Password, account.PasswordHash);
        Assert.NotEqual(PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(account, account.PasswordHash, model.Password));
        Assert.NotNull(await service.AuthenticateReaderAsync(model.Email, model.Password));
        Assert.IsType<ViewResult>(controller.RegisterSuccess());
        Assert.Equal("Đăng ký tài khoản thành công!", (string)controller.ViewBag.SuccessMessage);
        Assert.Equal("Chờ xác nhận email", (string)controller.ViewBag.AccountStatus);
        Assert.Contains("không cần chờ thư viện duyệt", (string)controller.ViewBag.Instruction);
    }

    [Fact]
    public async Task InvalidDateBindingDoesNotCreateAccount()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new ReaderRegistrationService(db, new PasswordHasher<ReaderAccount>(),
            NullLogger<ReaderRegistrationService>.Instance);
        var controller = CreateController(service);
        controller.ModelState.SetModelValue("DateOfBirth", "2026-02-30", "2026-02-30");
        controller.ModelState.AddModelError("DateOfBirth", "Invalid date");
        Assert.IsType<ViewResult>(await controller.Register(Valid()));
        Assert.Equal("Ngày sinh không hợp lệ.", controller.ModelState["DateOfBirth"]!.Errors.Single().ErrorMessage);
        Assert.Empty(await db.ReaderAccounts.ToListAsync());
    }

    private static ReaderRegistrationController CreateController(IReaderRegistrationService service)
    {
        var controller = new ReaderRegistrationController(service, new ReaderRegistrationIpRateLimiter(),
            new UnusedPasswordResetService(), new EphemeralDataProtectionProvider(), new NullAuditLogService(), new NoOpEmailVerificationService());
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.TempData = new TempDataDictionary(controller.HttpContext, new TestTempDataProvider());
        controller.Url = new FixedUrlHelper();
        return controller;
    }

    private sealed class UnusedPasswordResetService : IReaderPasswordResetService
    {
        public Task<bool> RequestAsync(string email, string resetUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsTokenValidAsync(string token, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ResetAsync(string token, string newPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int?> GetReaderIdForTokenAsync(string token, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NullAuditLogService : IAuditLogService
    {
        public Task WriteAsync(string actor, string action, string target, string? ipAddress, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<AuditLog>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuditLog>>([]);
        public Task<IReadOnlyList<AuditLog>> SearchAsync(AuditLogFilter filter, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuditLog>>([]);
        public Task<IReadOnlyList<string>> GetActorsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<AdminAccount?> GetSignedInStaffAsync(HttpRequest request, CancellationToken cancellationToken = default) => Task.FromResult<AdminAccount?>(null);
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

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}