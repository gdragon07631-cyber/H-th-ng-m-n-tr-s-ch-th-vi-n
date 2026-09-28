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

public sealed class PendingReaderHoldTests
{
    private const string PendingMessage = "Tài khoản của bạn đang chờ duyệt nên chưa thể đặt tài liệu. Vui lòng đến quầy thư viện và xuất trình giấy tờ để hoàn tất xác minh.";

    [Theory]
    [InlineData("/Book/Hold/1", false)]
    [InlineData("/ReaderRegistration/HoldDocument", false)]
    [InlineData("/api/documents/1/hold?readerId=2", true)]
    public async Task PendingReaderCanLoginButDirectHoldIsDeniedAndApprovalPreservesOldRules(string endpoint, bool api)
    {
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
        builder.Services.AddScoped<IBookService, BookService>();
        builder.Services.AddScoped<IAuthorService, AuthorService>();
        builder.Services.AddScoped<ICategoryService, CategoryService>();
        await using var app = builder.Build();
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reader = Reader(1, "Chờ duyệt");
            reader.PasswordHash = new PasswordHasher<ReaderAccount>().HashPassword(reader, "Abc12345");
            db.ReaderAccounts.AddRange(reader, Reader(2, "Đang hoạt động"));
            db.Authors.Add(new Author { Id = 1, Name = "Author" });
            db.Books.Add(new Book { Id = 1, Title = "Test Book", AuthorId = 1, Description = "Unchanged" });
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(address) };
            var loginHtml = await client.GetStringAsync("/ReaderRegistration/Login");
            using var login = await client.PostAsync("/ReaderRegistration/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["email"] = "reader1@example.com", ["password"] = "Abc12345",
                ["__RequestVerificationToken"] = Token(loginHtml)
            }));
            login.EnsureSuccessStatusCode();
            var profile = WebUtility.HtmlDecode(await login.Content.ReadAsStringAsync());
            Assert.EndsWith("/ReaderRegistration/Profile/1", login.RequestMessage!.RequestUri!.AbsolutePath);
            Assert.Contains("Chờ duyệt", profile);
            Assert.Contains("Hồ sơ của bạn đang chờ duyệt. Vui lòng đến quầy thư viện và xuất trình giấy tờ để nhân viên xác minh và kích hoạt tài khoản.", profile);
            Assert.Matches("<button[^>]*disabled[^>]*>Đặt tài liệu – Chưa khả dụng</button>", profile);
            var details = WebUtility.HtmlDecode(await client.GetStringAsync("/Book/Details/1"));
            Assert.Contains("Test Book", details);
            Assert.Matches("<button[^>]*disabled[^>]*>Đặt tài liệu – Chưa khả dụng</button>", details);
            var values = new Dictionary<string, string>
            {
                ["documentId"] = "1", ["readerId"] = "2", // Must not impersonate the approved reader via request parameters.
                ["__RequestVerificationToken"] = Token(profile)
            };
            using var denied = await client.PostAsync(endpoint, new FormUrlEncodedContent(values));
            Assert.Equal(api ? HttpStatusCode.Forbidden : HttpStatusCode.OK, denied.StatusCode);
            var deniedText = await denied.Content.ReadAsStringAsync();
            if (api)
            {
                using var json = System.Text.Json.JsonDocument.Parse(deniedText);
                Assert.Equal(PendingMessage, json.RootElement.GetProperty("message").GetString());
            }
            else Assert.Contains(PendingMessage, WebUtility.HtmlDecode(deniedText));
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                Assert.Empty(await db.BookHolds.ToListAsync());
                Assert.Empty(await db.BookCopies.ToListAsync());
                var book = await db.Books.SingleAsync();
                Assert.Equal("Test Book", book.Title);
                Assert.Equal("Unchanged", book.Description);
                // Simulate the existing approval process; no login refresh should be required.
                (await db.ReaderAccounts.FindAsync(1))!.Status = "Đang hoạt động";
                await db.SaveChangesAsync();
            }
            var approvedDetails = WebUtility.HtmlDecode(await client.GetStringAsync("/Book/Details/1"));
            Assert.Contains("Đặt giữ</button>", approvedDetails);
            Assert.DoesNotContain("Đặt tài liệu – Chưa khả dụng", approvedDetails);
            using var allowed = await client.PostAsync(endpoint, new FormUrlEncodedContent(values));
            allowed.EnsureSuccessStatusCode();
            using var duplicate = await client.PostAsync(endpoint, new FormUrlEncodedContent(values));
            Assert.Equal(api ? HttpStatusCode.Forbidden : HttpStatusCode.OK, duplicate.StatusCode);
            var duplicateText = await duplicate.Content.ReadAsStringAsync();
            if (api)
            {
                using var json = System.Text.Json.JsonDocument.Parse(duplicateText);
                Assert.Equal("Bạn đã đặt giữ cuốn sách này.", json.RootElement.GetProperty("message").GetString());
            }
            else Assert.Contains("Bạn đã đặt giữ cuốn sách này.", WebUtility.HtmlDecode(duplicateText));
            using var finalScope = app.Services.CreateScope();
            var finalDb = finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var hold = await finalDb.BookHolds.SingleAsync();
            Assert.Equal(1, hold.ReaderAccountId);
            Assert.Equal(1, hold.BookId);
        }
        finally { await app.StopAsync(); }
    }

    [Theory]
    [InlineData(999, "Đang hoạt động", "Không tìm thấy thông tin tài khoản Bạn đọc.")]
    [InlineData(1, "Chờ duyệt", PendingMessage)]
    [InlineData(1, "Đang hoạt động", "Không tìm thấy sách cần đặt giữ.")]
    public async Task MissingProfileOrBookNeverCreatesHold(int readerId, string status, string expectedMessage)
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.ReaderAccounts.Add(Reader(1, status));
        await db.SaveChangesAsync();
        var service = new ReaderRegistrationService(db, new PasswordHasher<ReaderAccount>(), NullLogger<ReaderRegistrationService>.Instance);
        var result = await service.HoldDocumentAsync(readerId, 999);
        Assert.False(result.IsAllowed);
        Assert.Equal(expectedMessage, result.Message);
        Assert.Empty(await db.BookHolds.ToListAsync());
        Assert.False(db.ChangeTracker.HasChanges());
    }

    private static ReaderAccount Reader(int id, string status) => new()
    {
        Id = id, FullName = "Test Reader", Email = $"reader{id}@example.com", DateOfBirth = new DateOnly(2000, 1, 1),
        PhoneNumber = "0912345678", StudentOrStaffCode = $"SV{id}", PasswordHash = "existing", Status = status
    };

    private static string Token(string html)
    {
        var input = Regex.Match(html, "<input\\b[^>]*\\bname=\"__RequestVerificationToken\"[^>]*>").Value;
        var match = Regex.Match(input, "\\bvalue=\"([^\"]*)\"");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
