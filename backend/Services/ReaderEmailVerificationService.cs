using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class ReaderEmailVerificationService(
    ApplicationDbContext dbContext,
    IEmailSender emailSender,
    IReaderRegistrationService registrationService,
    ILogger<ReaderEmailVerificationService> logger) : IReaderEmailVerificationService
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResendWindow = TimeSpan.FromHours(1);
    private const int MaxSendsPerWindow = 3;
    private static readonly TimeSpan SmtpTimeout = TimeSpan.FromSeconds(30);

    public Task<EmailVerificationSendResult> SendAsync(ReaderAccount reader, string confirmUrl, CancellationToken cancellationToken = default) =>
        // Tài khoản đã được tạo trước khi gọi hàm này: không để việc người dùng rời trang/tải lại
        // (hủy request) làm dở dang việc tạo liên kết và gửi email. Gửi SMTP có giới hạn thời gian riêng.
        CreateTokenAndSendAsync(reader, newEmail: null, confirmUrl, link =>
            $"<p>Xin chào {WebUtility.HtmlEncode(reader.FullName)},</p>" +
            "<p>Bạn vừa đăng ký tài khoản Bạn đọc tại thư viện. Hãy xác nhận đây là email của bạn trong vòng 24 giờ:</p>" +
            $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">Xác nhận email</a></p>" +
            "<p>Nếu bạn không đăng ký, hãy bỏ qua email này.</p>");

    public async Task<EmailVerificationSendResult> SendEmailChangeAsync(ReaderAccount reader, string confirmUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reader.PendingEmail))
            throw new InvalidOperationException("Bạn đọc không có yêu cầu đổi email đang chờ xác nhận.");
        var windowStart = DateTime.UtcNow.Subtract(ResendWindow);
        var recentSends = await dbContext.ReaderEmailVerificationTokens
            .CountAsync(item => item.ReaderAccountId == reader.Id && item.NewEmail != null && item.CreatedAtUtc > windowStart, cancellationToken);
        if (recentSends >= MaxSendsPerWindow) return new EmailVerificationSendResult(false, null, Throttled: true);

        var newEmail = reader.PendingEmail.Trim();
        return await CreateTokenAndSendAsync(reader, newEmail, confirmUrl, link =>
            $"<p>Xin chào {WebUtility.HtmlEncode(reader.FullName)},</p>" +
            "<p>Bạn vừa yêu cầu đổi email đăng nhập tài khoản Bạn đọc tại thư viện sang địa chỉ này. " +
            "Email chỉ được đổi sau khi bạn xác nhận trong vòng 24 giờ:</p>" +
            $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">Xác nhận email mới</a></p>" +
            "<p>Nếu bạn không yêu cầu, hãy bỏ qua email này; email cũ vẫn được giữ nguyên.</p>");
    }

    private async Task<EmailVerificationSendResult> CreateTokenAndSendAsync(
        ReaderAccount reader, string? newEmail, string confirmUrl, Func<string, string> buildBody)
    {
        var cancellationToken = CancellationToken.None;
        var now = DateTime.UtcNow;
        var isEmailChange = newEmail is not null;
        // Liên kết cũ cùng loại (xác nhận đăng ký / đổi email) chưa dùng hết hiệu lực ngay khi có liên kết mới.
        await dbContext.ReaderEmailVerificationTokens
            .Where(item => item.ReaderAccountId == reader.Id && item.UsedAtUtc == null && item.ExpiresAtUtc > now &&
                (item.NewEmail != null) == isEmailChange)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ExpiresAtUtc, now), cancellationToken);

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        dbContext.ReaderEmailVerificationTokens.Add(new ReaderEmailVerificationToken
        {
            ReaderAccountId = reader.Id,
            TokenHash = HashToken(token),
            NewEmail = newEmail,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(TokenLifetime)
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        var link = $"{confirmUrl}?token={Uri.EscapeDataString(token)}";
        using var sendTimeout = new CancellationTokenSource(SmtpTimeout);
        try
        {
            await emailSender.SendAsync(newEmail ?? reader.Email,
                isEmailChange ? "Xác nhận email mới cho tài khoản thư viện" : "Xác nhận email tài khoản thư viện",
                buildBody(link),
                sendTimeout.Token);
            return new EmailVerificationSendResult(true, link);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Không thể gửi email xác nhận cho bạn đọc #{ReaderId}.", reader.Id);
            return new EmailVerificationSendResult(false, link);
        }
    }

    public async Task<EmailVerificationSendResult?> ResendAsync(string email, string confirmUrl, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToUpperInvariant();
        var reader = await dbContext.ReaderAccounts
            .SingleOrDefaultAsync(item => item.Email.ToUpper() == normalized, cancellationToken);
        if (reader is null || reader.EmailConfirmed) return null;

        var windowStart = DateTime.UtcNow.Subtract(ResendWindow);
        var recentSends = await dbContext.ReaderEmailVerificationTokens
            .CountAsync(item => item.ReaderAccountId == reader.Id && item.CreatedAtUtc > windowStart, cancellationToken);
        if (recentSends >= MaxSendsPerWindow) return null;

        return await SendAsync(reader, confirmUrl, cancellationToken);
    }

    public async Task<EmailConfirmationOutcome> ConfirmAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return new(EmailConfirmationResult.InvalidOrExpired);
        var hash = HashToken(token);
        var stored = await dbContext.ReaderEmailVerificationTokens
            .Include(item => item.ReaderAccount)
            .SingleOrDefaultAsync(item => item.TokenHash == hash, cancellationToken);
        if (stored is null) return new(EmailConfirmationResult.InvalidOrExpired);
        if (stored.NewEmail is not null) return await ConfirmEmailChangeAsync(stored, cancellationToken);
        if (stored.ReaderAccount.EmailConfirmed) return new(EmailConfirmationResult.AlreadyConfirmed);
        if (stored.UsedAtUtc is not null || stored.ExpiresAtUtc <= DateTime.UtcNow) return new(EmailConfirmationResult.InvalidOrExpired);

        stored.UsedAtUtc = DateTime.UtcNow;
        stored.ReaderAccount.EmailConfirmed = true;
        stored.ReaderAccount.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var issuedCard = await ActivateAsync(stored.ReaderAccount, cancellationToken);
        return new(EmailConfirmationResult.Confirmed, issuedCard);
    }

    public async Task<EmailConfirmationOutcome> ConfirmByAdminAsync(int readerId, CancellationToken cancellationToken = default)
    {
        var reader = await dbContext.ReaderAccounts.SingleOrDefaultAsync(item => item.Id == readerId, cancellationToken);
        if (reader is null) return new(EmailConfirmationResult.InvalidOrExpired);
        if (reader.EmailConfirmed) return new(EmailConfirmationResult.AlreadyConfirmed, ReaderAccountId: reader.Id);

        var now = DateTime.UtcNow;
        // Liên kết xác nhận đăng ký còn trong email không cần dùng nữa.
        await dbContext.ReaderEmailVerificationTokens
            .Where(item => item.ReaderAccountId == reader.Id && item.NewEmail == null && item.UsedAtUtc == null && item.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ExpiresAtUtc, now), cancellationToken);
        reader.EmailConfirmed = true;
        reader.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        var issuedCard = await ActivateAsync(reader, cancellationToken);
        return new(EmailConfirmationResult.Confirmed, issuedCard, ReaderAccountId: reader.Id);
    }

    /// <summary>Đổi email đăng nhập sang địa chỉ mới chỉ khi chủ địa chỉ đó bấm liên kết xác nhận.</summary>
    private async Task<EmailConfirmationOutcome> ConfirmEmailChangeAsync(
        ReaderEmailVerificationToken stored, CancellationToken cancellationToken)
    {
        var reader = stored.ReaderAccount;
        var newEmail = stored.NewEmail!;
        if (string.Equals(reader.Email, newEmail, StringComparison.OrdinalIgnoreCase))
            return new(EmailConfirmationResult.AlreadyConfirmed);
        // Chỉ liên kết của yêu cầu đổi email mới nhất còn hiệu lực.
        if (stored.UsedAtUtc is not null || stored.ExpiresAtUtc <= DateTime.UtcNow ||
            !string.Equals(reader.PendingEmail, newEmail, StringComparison.OrdinalIgnoreCase))
            return new(EmailConfirmationResult.InvalidOrExpired);

        var normalized = newEmail.ToUpperInvariant();
        var taken = await dbContext.ReaderAccounts
            .AnyAsync(item => item.Id != reader.Id && item.Email.ToUpper() == normalized, cancellationToken);
        if (taken) return new(EmailConfirmationResult.EmailInUse);

        var previousEmail = reader.Email;
        stored.UsedAtUtc = DateTime.UtcNow;
        reader.Email = newEmail;
        reader.PendingEmail = null;
        reader.EmailConfirmed = true;
        reader.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(EmailConfirmationResult.EmailChanged, PreviousEmail: previousEmail, ReaderAccountId: reader.Id);
    }

    /// <summary>
    /// Không cần thủ thư duyệt: tài khoản Chờ duyệt được kích hoạt và cấp loại thẻ bạn đọc đã chọn, hạn 1 năm.
    /// Dùng lại nghiệp vụ duyệt sẵn có để sinh mã thẻ và chuyển trạng thái.
    /// </summary>
    private async Task<LibraryCard?> ActivateAsync(ReaderAccount reader, CancellationToken cancellationToken)
    {
        if (!string.Equals(reader.Status, ReaderStatusPending, StringComparison.OrdinalIgnoreCase)) return null;

        var activeTypes = await dbContext.LibraryCardTypes.AsNoTracking()
            .Where(type => type.IsActive).OrderBy(type => type.Id).Select(type => type.Id)
            .ToListAsync(cancellationToken);
        if (activeTypes.Count == 0)
        {
            logger.LogWarning("Không có loại thẻ nào đang hoạt động để cấp cho bạn đọc #{ReaderId}.", reader.Id);
            return null;
        }

        var cardTypeId = reader.RequestedLibraryCardTypeId is { } requested && activeTypes.Contains(requested)
            ? requested
            : activeTypes[0];
        var today = DateOnly.FromDateTime(DateTime.Today);
        var outcome = await registrationService.ApproveReaderAsync(new ApproveReaderViewModel
        {
            ReaderAccountId = reader.Id,
            LibraryCardTypeId = cardTypeId,
            IssuedOn = today,
            ExpiresOn = today.AddYears(1)
        }, cancellationToken);

        if (!outcome.IsSuccess)
        {
            logger.LogWarning("Không thể kích hoạt tự động bạn đọc #{ReaderId}: {Reason}", reader.Id, outcome.ErrorMessage);
            return null;
        }
        return outcome.LibraryCard;
    }

    private const string ReaderStatusPending = "Chờ duyệt";

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
