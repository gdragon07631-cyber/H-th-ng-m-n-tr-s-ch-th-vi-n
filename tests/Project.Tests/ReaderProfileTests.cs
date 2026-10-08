using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Tests;

public sealed class ReaderProfileTests
{
    [Fact]
    public async Task ChangingEmailWithCurrentPasswordKeepsOldEmailUntilConfirmed()
    {
        using var db = CreateDb();
        var reader = CreateReader();
        db.ReaderAccounts.Add(reader);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.UpdateReaderContactAsync(reader.Id, "222", "New address", "new@example.com", "correct-password");

        Assert.Equal(ReaderContactUpdateResult.EmailChangePending, result);
        // Email chỉ đổi khi chủ địa chỉ mới bấm liên kết xác nhận.
        Assert.Equal("old@example.com", reader.Email);
        Assert.Equal("new@example.com", reader.PendingEmail);
        Assert.Equal("222", reader.PhoneNumber);
        Assert.Equal("New address", reader.Address);
    }

    [Fact]
    public async Task ChangingEmailToAddressOfAnotherAccountIsRejected()
    {
        using var db = CreateDb();
        var reader = CreateReader();
        var other = CreateReader();
        other.Email = "taken@example.com";
        other.StudentOrStaffCode = "R-2";
        db.ReaderAccounts.AddRange(reader, other);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.UpdateReaderContactAsync(reader.Id, "222", "New address", "TAKEN@example.com", "correct-password");

        Assert.Equal(ReaderContactUpdateResult.EmailInUse, result);
        Assert.Null(reader.PendingEmail);
        Assert.Equal("111", reader.PhoneNumber);
    }

    [Fact]
    public async Task ChangingEmailWithWrongPasswordLeavesAllContactDataUnchanged()
    {
        using var db = CreateDb();
        var reader = CreateReader();
        db.ReaderAccounts.Add(reader);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.UpdateReaderContactAsync(reader.Id, "222", "New address", "new@example.com", "wrong-password");

        Assert.Equal(ReaderContactUpdateResult.InvalidCurrentPassword, result);
        Assert.Equal("old@example.com", reader.Email);
        Assert.Equal("111", reader.PhoneNumber);
        Assert.Equal("Old address", reader.Address);
    }

    [Fact]
    public async Task PhoneCanBeUpdatedWithoutPasswordWhenEmailIsUnchanged()
    {
        using var db = CreateDb();
        var reader = CreateReader();
        db.ReaderAccounts.Add(reader);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.UpdateReaderContactAsync(reader.Id, "222", "Old address", "old@example.com", "");

        Assert.Equal(ReaderContactUpdateResult.Success, result);
        Assert.Equal("old@example.com", reader.Email);
        Assert.Equal("222", reader.PhoneNumber);
        Assert.Equal("Old address", reader.Address);
    }

    [Fact]
    public async Task AddressCanBeUpdatedWithoutPasswordWhenEmailIsUnchanged()
    {
        using var db = CreateDb();
        var reader = CreateReader();
        db.ReaderAccounts.Add(reader);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.UpdateReaderContactAsync(reader.Id, "111", "New address", "old@example.com", "");

        Assert.Equal(ReaderContactUpdateResult.Success, result);
        Assert.Equal("old@example.com", reader.Email);
        Assert.Equal("111", reader.PhoneNumber);
        Assert.Equal("New address", reader.Address);
    }

    [Fact]
    public async Task ChangedEmailWithBlankPasswordIsRejected()
    {
        using var db = CreateDb();
        var reader = CreateReader();
        db.ReaderAccounts.Add(reader);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.UpdateReaderContactAsync(reader.Id, "111", "Old address", "new@example.com", "");

        Assert.Equal(ReaderContactUpdateResult.InvalidCurrentPassword, result);
        Assert.Equal("old@example.com", reader.Email);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("abc@")]
    [InlineData("@gmail.com")]
    [InlineData("abc@gmail")]
    [InlineData("abc @gmail.com")]
    public void InvalidEmailFormatsFailProfileValidation(string email)
    {
        var model = new ReaderProfileViewModel
        {
            FullName = "Reader",
            DateOfBirth = new DateOnly(2000, 1, 1),
            StudentOrStaffCode = "R-1",
            Status = "Đang hoạt động",
            PhoneNumber = "111",
            Address = "Address",
            Email = email
        };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var valid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            model, new System.ComponentModel.DataAnnotations.ValidationContext(model), results, validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(model.Email)));
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ReaderRegistrationService CreateService(ApplicationDbContext db) => new(
        db,
        new PasswordHasher<ReaderAccount>(),
        NullLogger<ReaderRegistrationService>.Instance);

    private static ReaderAccount CreateReader()
    {
        var hasher = new PasswordHasher<ReaderAccount>();
        var reader = new ReaderAccount
        {
            FullName = "Test Reader",
            DateOfBirth = new DateOnly(2000, 1, 1),
            Email = "old@example.com",
            PhoneNumber = "111",
            Address = "Old address",
            StudentOrStaffCode = "R-1",
            Status = "Đang hoạt động"
        };
        reader.PasswordHash = hasher.HashPassword(reader, "correct-password");
        return reader;
    }
}
