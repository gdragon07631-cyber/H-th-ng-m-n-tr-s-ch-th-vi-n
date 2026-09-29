using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class StaffAccountService(
    ApplicationDbContext dbContext,
    IPasswordHasher<AdminAccount> passwordHasher,
    IEmailSender emailSender,
    ILogger<StaffAccountService> logger) : IStaffAccountService
{
    public static readonly TimeSpan SetupTokenLifetime = TimeSpan.FromHours(24);

    public async Task<IReadOnlyList<AdminAccount>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.AdminAccounts.AsNoTracking()
            .OrderBy(account => account.Role).ThenBy(account => account.FullName).ThenBy(account => account.Email)
            .ToListAsync(cancellationToken);

    public Task<AdminAccount?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        dbContext.AdminAccounts.AsNoTracking().SingleOrDefaultAsync(account => account.Id == id, cancellationToken);

    public async Task<StaffAccountResult> CreateAsync(StaffAccountFormViewModel model, string setupUrl, CancellationToken cancellationToken = default)
    {
        if (!AccountRoles.IsValid(model.Role)) return InvalidRole();
        var email = model.Email.Trim();
        if (await EmailInUseAsync(email, exceptId: null, cancellationToken)) return DuplicateEmail(email);

        var account = new AdminAccount
        {
            FullName = model.FullName.Trim(),
            Email = email,
            PhoneNumber = model.PhoneNumber.Trim(),
            Role = model.Role,
            IsActive = model.IsActive,
            PasswordHash = string.Empty // chưa đăng nhập được cho tới khi đặt mật khẩu qua email
        };
        dbContext.AdminAccounts.Add(account);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Hai yêu cầu tạo cùng email chạy song song: unique index chặn bản ghi thứ hai.
            dbContext.Entry(account).State = EntityState.Detached;
            if (await EmailInUseAsync(email, exceptId: null, cancellationToken)) return DuplicateEmail(email);
            throw;
        }

        var (sent, link) = await IssueSetupTokenAsync(account, setupUrl, cancellationToken);
        return new StaffAccountResult(StaffAccountStatus.Success, account) { EmailSent = sent, SetupLink = link };
    }

    public async Task<StaffAccountResult> UpdateAsync(int id, StaffAccountFormViewModel model, int actingAdminId, CancellationToken cancellationToken = default)
    {
        if (!AccountRoles.IsValid(model.Role)) return InvalidRole();
        var account = await dbContext.AdminAccounts.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (account is null) return new StaffAccountResult(StaffAccountStatus.NotFound, ErrorMessage: "Không tìm thấy tài khoản.");

        if (id == actingAdminId && (model.Role != account.Role || !model.IsActive))
            return CannotChangeOwnAccess();

        var email = model.Email.Trim();
        if (await EmailInUseAsync(email, id, cancellationToken)) return DuplicateEmail(email);

        var deactivating = account.IsActive && !model.IsActive;
        account.FullName = model.FullName.Trim();
        account.Email = email;
        account.PhoneNumber = model.PhoneNumber.Trim();
        account.Role = model.Role;
        account.IsActive = model.IsActive;
        if (deactivating) await RevokeSessionsAsync(account.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new StaffAccountResult(StaffAccountStatus.Success, account);
    }

    public async Task<StaffAccountResult> SetActiveAsync(int id, bool isActive, int actingAdminId, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.AdminAccounts.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (account is null) return new StaffAccountResult(StaffAccountStatus.NotFound, ErrorMessage: "Không tìm thấy tài khoản.");
        if (id == actingAdminId && !isActive) return CannotChangeOwnAccess();

        if (account.IsActive != isActive)
        {
            account.IsActive = isActive;
            if (!isActive) await RevokeSessionsAsync(account.Id, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return new StaffAccountResult(StaffAccountStatus.Success, account);
    }

    public async Task<StaffAccountResult> ResendSetupEmailAsync(int id, string setupUrl, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.AdminAccounts.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (account is null) return new StaffAccountResult(StaffAccountStatus.NotFound, ErrorMessage: "Không tìm thấy tài khoản.");
        if (!string.IsNullOrEmpty(account.PasswordHash))
            return new StaffAccountResult(StaffAccountStatus.PasswordAlreadySet, account, "Tài khoản này đã đặt mật khẩu.");

        var (sent, link) = await IssueSetupTokenAsync(account, setupUrl, cancellationToken);
        return new StaffAccountResult(StaffAccountStatus.Success, account) { EmailSent = sent, SetupLink = link };
    }

    public Task<bool> IsSetupTokenValidAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return Task.FromResult(false);
        var hash = HashToken(token);
        var now = DateTime.UtcNow;
        return dbContext.StaffPasswordSetupTokens.AnyAsync(
            item => item.TokenHash == hash && item.UsedAtUtc == null && item.ExpiresAtUtc > now, cancellationToken);
    }

    public async Task<AdminAccount?> CompleteSetupAsync(string token, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var hash = HashToken(token);
        var now = DateTime.UtcNow;
        var setupToken = await dbContext.StaffPasswordSetupTokens
            .Include(item => item.AdminAccount)
            .SingleOrDefaultAsync(item => item.TokenHash == hash && item.UsedAtUtc == null && item.ExpiresAtUtc > now, cancellationToken);
        if (setupToken is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var account = setupToken.AdminAccount;
        account.PasswordHash = passwordHasher.HashPassword(account, password);
        account.FailedLoginAttempts = 0;
        account.LockoutStartUtc = null;
        account.LockoutEndUtc = null;
        await dbContext.StaffPasswordSetupTokens
            .Where(item => item.AdminAccountId == account.Id && item.UsedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.UsedAtUtc, now), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return account;
    }

    private async Task<(bool Sent, string Link)> IssueSetupTokenAsync(AdminAccount account, string setupUrl, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        // Liên kết cũ chưa dùng hết hiệu lực ngay khi có liên kết mới.
        await dbContext.StaffPasswordSetupTokens
            .Where(item => item.AdminAccountId == account.Id && item.UsedAtUtc == null && item.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ExpiresAtUtc, now), cancellationToken);

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        dbContext.StaffPasswordSetupTokens.Add(new StaffPasswordSetupToken
        {
            AdminAccountId = account.Id,
            TokenHash = HashToken(token),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(SetupTokenLifetime)
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        var link = $"{setupUrl}?token={Uri.EscapeDataString(token)}";
        try
        {
            await emailSender.SendAsync(account.Email, "Đặt mật khẩu tài khoản thư viện",
                $"<p>Xin chào {WebUtility.HtmlEncode(account.FullName)},</p>" +
                $"<p>Quản trị hệ thống đã tạo cho bạn tài khoản <strong>{WebUtility.HtmlEncode(AccountRoles.DisplayName(account.Role))}</strong>.</p>" +
                $"<p>Hãy đặt mật khẩu trong vòng 24 giờ: <a href=\"{WebUtility.HtmlEncode(link)}\">Đặt mật khẩu</a></p>",
                cancellationToken);
            return (true, link);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Không thể gửi email đặt mật khẩu cho tài khoản nhân sự #{AccountId}.", account.Id);
            return (false, link);
        }
    }

    private Task RevokeSessionsAsync(int accountId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return dbContext.RefreshTokens
            .Where(item => item.AdminAccountId == accountId && item.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.RevokedAtUtc, now), cancellationToken);
    }

    private Task<bool> EmailInUseAsync(string email, int? exceptId, CancellationToken cancellationToken)
    {
        var normalized = email.ToUpperInvariant();
        return dbContext.AdminAccounts.AnyAsync(
            account => account.Email.ToUpper() == normalized && (exceptId == null || account.Id != exceptId), cancellationToken);
    }

    private static StaffAccountResult DuplicateEmail(string email) =>
        new(StaffAccountStatus.DuplicateEmail, ErrorMessage: $"Email {email} đang được dùng bởi một tài khoản khác.");

    private static StaffAccountResult InvalidRole() =>
        new(StaffAccountStatus.InvalidRole, ErrorMessage: "Vai trò phải là Thủ thư, Quản lý thư viện hoặc Quản trị hệ thống.");

    private static StaffAccountResult CannotChangeOwnAccess() =>
        new(StaffAccountStatus.CannotChangeOwnAccess, ErrorMessage: "Bạn không thể tự khoá hoặc tự đổi vai trò của chính mình.");

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
