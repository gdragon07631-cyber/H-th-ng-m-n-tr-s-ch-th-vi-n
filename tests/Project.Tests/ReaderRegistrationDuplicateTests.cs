using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Controllers;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

public sealed class ReaderRegistrationDuplicateTests
{
    [Theory]
    [InlineData("STUDENT@example.com", "NEW001", true, false)]
    [InlineData("new@example.com", "sv001", false, true)]
    [InlineData("new@example.com", "cb001", false, true)]
    [InlineData("STUDENT@example.com", "sv001", true, true)]
    [InlineData("STUDENT@example.com", "cb001", true, true)]
    public async Task DuplicatePostPreservesFormRendersRecoveryLinkAndDoesNotInsert(
        string email, string code, bool emailDuplicate, bool codeDuplicate)
    {
        // Exercise MVC binding, antiforgery, the actual service and compiled Razor views.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(ReaderRegistrationController).Assembly.GetName().Name,
            EnvironmentName = "Development"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddControllersWithViews();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        var databaseName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.AddScoped<IReaderRegistrationService, ReaderRegistrationService>();
        builder.Services.AddScoped<IPasswordHasher<ReaderAccount>, PasswordHasher<ReaderAccount>>();
        builder.Services.AddSingleton<ReaderRegistrationIpRateLimiter>();
        await using var app = builder.Build();
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ReaderAccounts.AddRange(Existing("student@example.com", "SV001"), Existing("staff@example.com", "CB001"));
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(address) };
            var initialHtml = await client.GetStringAsync("/ReaderRegistration/Register");
            var form = new Dictionary<string, string>
            {
                ["FullName"] = "Nguyễn Văn A", ["DateOfBirth"] = "2004-10-10",
                ["Email"] = email, ["PhoneNumber"] = "0987654321", ["StudentOrStaffCode"] = code,
                ["Password"] = "Abc12345", ["ConfirmPassword"] = "Abc12345",
                ["__RequestVerificationToken"] = InputValue(initialHtml, "__RequestVerificationToken")
            };
            using var response = await client.PostAsync("/ReaderRegistration/Register", new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            var decoded = WebUtility.HtmlDecode(html);
            foreach (var field in new[] { "FullName", "DateOfBirth", "Email", "PhoneNumber", "StudentOrStaffCode" })
                Assert.Equal(form[field], InputValue(html, field));
            Assert.Equal(emailDuplicate, decoded.Contains("Email này đã được đăng ký. Nếu đây là tài khoản của bạn, hãy sử dụng chức năng Quên mật khẩu."));
            Assert.Equal(codeDuplicate, decoded.Contains("Mã sinh viên/mã cán bộ này đã được đăng ký. Nếu đây là tài khoản của bạn, hãy sử dụng chức năng Quên mật khẩu."));
            Assert.Matches("<a[^>]*href=\"/ReaderRegistration/ForgotPassword\"[^>]*>Quên mật khẩu</a>", decoded);
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                Assert.Equal(2, await db.ReaderAccounts.CountAsync());
                Assert.Empty(await db.LibraryCards.ToListAsync());
            }
            var recoveryHtml = WebUtility.HtmlDecode(await client.GetStringAsync("/ReaderRegistration/ForgotPassword"));
            Assert.Contains("Chức năng khôi phục mật khẩu trực tuyến hiện chưa được triển khai.", recoveryHtml);

            // Correct only the duplicated values and resubmit the preserved form.
            form["Email"] = "corrected@example.com";
            form["StudentOrStaffCode"] = "CORRECTED001";
            form["__RequestVerificationToken"] = InputValue(html, "__RequestVerificationToken");
            using var retry = await client.PostAsync("/ReaderRegistration/Register", new FormUrlEncodedContent(form));
            retry.EnsureSuccessStatusCode();
            var success = WebUtility.HtmlDecode(await retry.Content.ReadAsStringAsync());
            Assert.Contains("Đăng ký tài khoản thành công!", success);
            using var finalScope = app.Services.CreateScope();
            var finalDb = finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(3, await finalDb.ReaderAccounts.CountAsync());
            var account = await finalDb.ReaderAccounts.SingleAsync(reader => reader.Email == form["Email"]);
            Assert.Equal("Chờ duyệt", account.Status);
            Assert.Equal(form["FullName"], account.FullName);
            Assert.NotEqual(form["Password"], account.PasswordHash);

            // The initial duplicate and corrected submission consumed two slots.
            using var third = await client.PostAsync("/ReaderRegistration/Register", new FormUrlEncodedContent(form));
            Assert.Contains("Email này đã được đăng ký.", WebUtility.HtmlDecode(await third.Content.ReadAsStringAsync()));
            client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.168.1.99");
            form["Email"] = "fourth@example.com";
            form["StudentOrStaffCode"] = "FOURTH001";
            using var fourth = await client.PostAsync("/ReaderRegistration/Register", new FormUrlEncodedContent(form));
            var blockedHtml = await fourth.Content.ReadAsStringAsync();
            Assert.Contains("Bạn đã gửi quá nhiều yêu cầu đăng ký. Vui lòng thử lại sau.", WebUtility.HtmlDecode(blockedHtml));
            foreach (var field in new[] { "FullName", "DateOfBirth", "Email", "PhoneNumber", "StudentOrStaffCode" })
                Assert.Equal(form[field], InputValue(blockedHtml, field));
            Assert.Equal(3, await finalDb.ReaderAccounts.CountAsync());
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Theory]
    [InlineData("Chờ duyệt")]
    [InlineData("Đang hoạt động")]
    [InlineData("Từ chối")]
    public async Task ComparisonTrimsAndIgnoresCaseWithoutChangingExistingAccount(string status)
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var existing = Existing("  Student@Example.com  ", "  SV001  ");
        existing.Status = status;
        db.ReaderAccounts.Add(existing);
        await db.SaveChangesAsync();
        var service = new ReaderRegistrationService(db, new PasswordHasher<ReaderAccount>(),
            NullLogger<ReaderRegistrationService>.Instance);
        var result = await service.RegisterAsync(new ReaderRegistrationViewModel
        {
            FullName = "New Reader", DateOfBirth = new DateOnly(2004, 10, 10),
            Email = " student@example.COM ", PhoneNumber = "0987654321", StudentOrStaffCode = " sv001 ",
            Password = "Abc12345", ConfirmPassword = "Abc12345"
        });
        Assert.False(result.IsSuccess);
        Assert.True(result.IsEmailDuplicate);
        Assert.True(result.IsCodeDuplicate);
        Assert.Null(result.Account);
        db.ChangeTracker.Clear();
        var saved = await db.ReaderAccounts.SingleAsync();
        Assert.Equal("  Student@Example.com  ", saved.Email);
        Assert.Equal("  SV001  ", saved.StudentOrStaffCode);
        Assert.Equal(status, saved.Status);
        Assert.Equal("existing-hash", saved.PasswordHash);
    }

    private static ReaderAccount Existing(string email, string code) => new()
    {
        FullName = "Existing Reader", DateOfBirth = new DateOnly(2000, 1, 1),
        Email = email, PhoneNumber = "0912345678", StudentOrStaffCode = code, PasswordHash = "existing-hash"
    };

    private static string InputValue(string html, string name)
    {
        var input = Regex.Match(html, "<input\\b[^>]*\\bname=\"" + Regex.Escape(name) + "\"[^>]*>").Value;
        Assert.NotEmpty(input);
        var value = Regex.Match(input, "\\bvalue=\"([^\"]*)\"");
        Assert.True(value.Success, $"Missing value for {name}");
        return WebUtility.HtmlDecode(value.Groups[1].Value);
    }
}
