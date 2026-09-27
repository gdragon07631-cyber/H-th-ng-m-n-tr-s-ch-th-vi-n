using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

public sealed class ReaderPasswordChangeTests
{
    [Fact]
    public async Task ValidPasswordChangeStoresHashesAndAllowsLoginWithNewPassword()
    {
        using var db = CreateDb();
        var (reader, hasher) = CreateReader("current-password");
        db.ReaderAccounts.Add(reader);
        await db.SaveChangesAsync();
        var service = CreateService(db, hasher);

        var result = await service.ChangeReaderPasswordAsync(reader.Id, "current-password", "new-password");

        Assert.Equal(ReaderPasswordChangeResult.Success, result);
        Assert.Equal(PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(reader, reader.PasswordHash, "new-password"));
        Assert.Single(db.ReaderPasswordHistories);
        var savedHistory = await db.ReaderPasswordHistories.SingleAsync();
        Assert.Equal(PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(reader, savedHistory.PasswordHash, "current-password"));
        Assert.DoesNotContain("current-password", savedHistory.PasswordHash);
        Assert.DoesNotContain("new-password", reader.PasswordHash);

        Assert.NotNull(await service.AuthenticateReaderAsync("reader@example.com", "new-password"));
        Assert.Null(await service.AuthenticateReaderAsync("reader@example.com", "current-password"));
    }

    [Fact]
    public async Task IncorrectCurrentPasswordDoesNotChangePasswordOrHistory()
    {
        using var db = CreateDb();
        var (reader, hasher) = CreateReader("current-password");
        db.ReaderAccounts.Add(reader);
        db.ReaderPasswordHistories.Add(CreateHistory(reader.Id, "older-password", 1));
        await db.SaveChangesAsync();
        var originalHash = reader.PasswordHash;
        var service = CreateService(db, hasher);

        var result = await service.ChangeReaderPasswordAsync(reader.Id, "wrong-password", "new-password");

        Assert.Equal(ReaderPasswordChangeResult.IncorrectCurrentPassword, result);
        Assert.Equal(originalHash, reader.PasswordHash);
        Assert.Single(db.ReaderPasswordHistories);
        Assert.NotNull(await service.AuthenticateReaderAsync("reader@example.com", "current-password"));
        Assert.Null(await service.AuthenticateReaderAsync("reader@example.com", "new-password"));
    }

    [Fact]
    public void MismatchedConfirmationFailsModelValidation()
    {
        var model = new ReaderChangePasswordViewModel
        {
            CurrentPassword = "current-password",
            NewPassword = "new-password",
            ConfirmNewPassword = "different-password"
        };
        var errors = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var valid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            model, new System.ComponentModel.DataAnnotations.ValidationContext(model), errors, validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(errors, error => error.ErrorMessage == "Mật khẩu mới và mật khẩu xác nhận không trùng khớp.");
    }

    [Theory]
    [InlineData("CurrentPassword")]
    [InlineData("NewPassword")]
    [InlineData("ConfirmNewPassword")]
    public void EachPasswordFieldIsRequired(string emptyProperty)
    {
        var model = new ReaderChangePasswordViewModel
        {
            CurrentPassword = emptyProperty == "CurrentPassword" ? string.Empty : "current-password",
            NewPassword = emptyProperty == "NewPassword" ? string.Empty : "new-password",
            ConfirmNewPassword = emptyProperty == "ConfirmNewPassword" ? string.Empty : "new-password"
        };
        var errors = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var valid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            model, new System.ComponentModel.DataAnnotations.ValidationContext(model), errors, validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(errors, error => error.MemberNames.Contains(emptyProperty));
    }

    [Fact]
    public async Task PasswordInThreeMostRecentCannotBeReused()
    {
        using var db = CreateDb();
        var (reader, hasher) = CreateReader("password-d");
        db.ReaderAccounts.Add(reader);
        db.ReaderPasswordHistories.AddRange(
            CreateHistory(reader.Id, "password-a", 1),
            CreateHistory(reader.Id, "password-b", 2),
            CreateHistory(reader.Id, "password-c", 3));
        await db.SaveChangesAsync();
        var originalHash = reader.PasswordHash;
        var service = CreateService(db, hasher);

        var result = await service.ChangeReaderPasswordAsync(reader.Id, "password-d", "password-b");

        Assert.Equal(ReaderPasswordChangeResult.PasswordRecentlyUsed, result);
        Assert.Equal(ReaderPasswordChangeResult.PasswordRecentlyUsed,
            await service.ChangeReaderPasswordAsync(reader.Id, "password-d", "password-a"));
        Assert.Equal(originalHash, reader.PasswordHash);
        Assert.Equal(3, await db.ReaderPasswordHistories.CountAsync());
    }

    [Fact]
    public async Task SuccessfulChangeRetainsOnlyThreeNewestPreviousHashes()
    {
        using var db = CreateDb();
        var (reader, hasher) = CreateReader("password-d");
        db.ReaderAccounts.Add(reader);
        db.ReaderPasswordHistories.AddRange(
            CreateHistory(reader.Id, "password-a", 1),
            CreateHistory(reader.Id, "password-b", 2),
            CreateHistory(reader.Id, "password-c", 3));
        await db.SaveChangesAsync();
        var service = CreateService(db, hasher);

        var result = await service.ChangeReaderPasswordAsync(reader.Id, "password-d", "password-e");

        Assert.Equal(ReaderPasswordChangeResult.Success, result);
        var newestFirst = await db.ReaderPasswordHistories
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .ThenByDescending(entry => entry.Id)
            .ToListAsync();
        Assert.Equal(3, newestFirst.Count);
        Assert.True(hasher.VerifyHashedPassword(reader, newestFirst[0].PasswordHash, "password-d") != PasswordVerificationResult.Failed);
        Assert.True(hasher.VerifyHashedPassword(reader, newestFirst[1].PasswordHash, "password-c") != PasswordVerificationResult.Failed);
        Assert.True(hasher.VerifyHashedPassword(reader, newestFirst[2].PasswordHash, "password-b") != PasswordVerificationResult.Failed);

        // The removed fourth-oldest password is no longer among the three most recent.
        Assert.Equal(ReaderPasswordChangeResult.Success,
            await service.ChangeReaderPasswordAsync(reader.Id, "password-e", "password-a"));
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ReaderRegistrationService CreateService(ApplicationDbContext db, PasswordHasher<ReaderAccount> hasher) => new(
        db,
        hasher,
        NullLogger<ReaderRegistrationService>.Instance);

    private static (ReaderAccount Reader, PasswordHasher<ReaderAccount> Hasher) CreateReader(string password)
    {
        var hasher = new PasswordHasher<ReaderAccount>();
        var reader = new ReaderAccount
        {
            FullName = "Test Reader",
            DateOfBirth = new DateOnly(2000, 1, 1),
            Email = "reader@example.com",
            PhoneNumber = "111",
            StudentOrStaffCode = "R-1",
            Status = "Đang hoạt động"
        };
        reader.PasswordHash = hasher.HashPassword(reader, password);
        return (reader, hasher);
    }

    private static ReaderPasswordHistory CreateHistory(int readerId, string password, int day)
    {
        var hasher = new PasswordHasher<ReaderAccount>();
        var reader = new ReaderAccount { Id = readerId };
        return new ReaderPasswordHistory
        {
            ReaderAccountId = readerId,
            PasswordHash = hasher.HashPassword(reader, password),
            CreatedAtUtc = new DateTime(2025, 1, day, 0, 0, 0, DateTimeKind.Utc)
        };
    }
}
