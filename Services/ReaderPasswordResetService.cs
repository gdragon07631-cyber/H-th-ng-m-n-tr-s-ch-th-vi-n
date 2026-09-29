using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class ReaderPasswordResetService(
    ApplicationDbContext dbContext,
    IPasswordHasher<ReaderAccount> passwordHasher,
    IEmailSender emailSender,
    ILogger<ReaderPasswordResetService> logger) : IReaderPasswordResetService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RequestWindow = TimeSpan.FromHours(1);
    private const int MaxRequestsPerWindow = 3;

    public async Task<bool> RequestAsync(string email, string resetUrl, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var emailHash = HashToken(normalizedEmail);
        var now = DateTime.UtcNow;
        var windowStart = now.Subtract(RequestWindow);

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken))
        {
            await dbContext.ReaderPasswordResetRequests
                .Where(request => request.EmailHash == emailHash && request.RequestedAtUtc <= windowStart)
                .ExecuteDeleteAsync(cancellationToken);
            var recentRequests = await dbContext.ReaderPasswordResetRequests
                .CountAsync(request => request.EmailHash == emailHash && request.RequestedAtUtc > windowStart, cancellationToken);
            dbContext.ReaderPasswordResetRequests.Add(new ReaderPasswordResetRequest
            {
                EmailHash = emailHash,
                RequestedAtUtc = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (recentRequests >= MaxRequestsPerWindow) return false;
        }

        var reader = await dbContext.ReaderAccounts.SingleOrDefaultAsync(
            item => item.Email.ToUpper() == normalizedEmail, cancellationToken);
        if (reader is null) return true;

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        dbContext.ReaderPasswordResetTokens.Add(new ReaderPasswordResetToken
        {
            ReaderAccountId = reader.Id,
            TokenHash = HashToken(token),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.Add(TokenLifetime)
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        var link = $"{resetUrl}?token={Uri.EscapeDataString(token)}";
        try
        {
            await emailSender.SendAsync(reader.Email, "Đặt lại mật khẩu",
                $"<p>Để đặt lại mật khẩu Bạn đọc, hãy mở liên kết sau trong vòng 30 phút:</p><p><a href=\"{System.Net.WebUtility.HtmlEncode(link)}\">Đặt lại mật khẩu</a></p>",
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Keep the public response identical whether the address exists or SMTP delivery fails.
            logger.LogError(exception, "Không thể gửi email đặt lại mật khẩu.");
        }
        return true;
    }

    public Task<bool> IsTokenValidAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return Task.FromResult(false);
        var hash = HashToken(token);
        var now = DateTime.UtcNow;
        return dbContext.ReaderPasswordResetTokens.AnyAsync(
            item => item.TokenHash == hash && item.UsedAtUtc == null && item.ExpiresAtUtc > now,
            cancellationToken);
    }

    public async Task<bool> ResetAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var hash = HashToken(token);
        var now = DateTime.UtcNow;
        var resetToken = await dbContext.ReaderPasswordResetTokens
            .Include(item => item.ReaderAccount)
            .SingleOrDefaultAsync(item => item.TokenHash == hash && item.UsedAtUtc == null && item.ExpiresAtUtc > now, cancellationToken);
        if (resetToken is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        resetToken.ReaderAccount.PasswordHash = passwordHasher.HashPassword(resetToken.ReaderAccount, newPassword);
        resetToken.ReaderAccount.SessionVersion++;
        resetToken.UsedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
