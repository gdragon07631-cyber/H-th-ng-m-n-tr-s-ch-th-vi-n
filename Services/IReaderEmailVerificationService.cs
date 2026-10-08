using Project.Models;

namespace Project.Services;

public enum EmailConfirmationResult
{
    Confirmed,
    AlreadyConfirmed,
    InvalidOrExpired,
    /// <summary>Email đăng nhập đã được đổi sang địa chỉ mới vừa xác nhận.</summary>
    EmailChanged,
    /// <summary>Địa chỉ mới đã được tài khoản khác dùng trong lúc chờ xác nhận.</summary>
    EmailInUse
}

/// <summary>Kết quả xác nhận email; <see cref="IssuedCard"/> là thẻ được cấp tự động khi tài khoản được kích hoạt.</summary>
public sealed record EmailConfirmationOutcome(
    EmailConfirmationResult Result,
    LibraryCard? IssuedCard = null,
    string? PreviousEmail = null,
    int? ReaderAccountId = null);

/// <summary>
/// Kết quả gửi email xác nhận. <see cref="Link"/> chỉ dùng để hiển thị ở môi trường phát triển khi chưa cấu hình SMTP.
/// <see cref="Throttled"/> = đã gửi quá số lần cho phép trong một giờ, không gửi thêm.
/// </summary>
public sealed record EmailVerificationSendResult(bool Sent, string? Link, bool Throttled = false);

public interface IReaderEmailVerificationService
{
    /// <summary>Tạo liên kết xác nhận (hết hạn sau 24 giờ, dùng một lần) và gửi tới email của bạn đọc.</summary>
    Task<EmailVerificationSendResult> SendAsync(ReaderAccount reader, string confirmUrl, CancellationToken cancellationToken = default);

    /// <summary>Gửi liên kết xác nhận tới email mới (<see cref="ReaderAccount.PendingEmail"/>); email chỉ đổi khi bấm liên kết.</summary>
    Task<EmailVerificationSendResult> SendEmailChangeAsync(ReaderAccount reader, string confirmUrl, CancellationToken cancellationToken = default);

    /// <summary>Gửi lại email xác nhận nếu email tồn tại và chưa xác nhận; giới hạn 3 lần mỗi giờ.</summary>
    Task<EmailVerificationSendResult?> ResendAsync(string email, string confirmUrl, CancellationToken cancellationToken = default);

    /// <summary>Xác nhận email; tài khoản tự đăng ký đang Chờ duyệt được kích hoạt và cấp thẻ (hạn 1 năm) ngay lúc này.</summary>
    Task<EmailConfirmationOutcome> ConfirmAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Quản trị xác nhận email thay bạn đọc (ví dụ bạn đọc không nhận được email nhưng đã đến quầy chứng minh);
    /// tài khoản Chờ duyệt cũng được kích hoạt và cấp thẻ như khi bấm liên kết.
    /// </summary>
    Task<EmailConfirmationOutcome> ConfirmByAdminAsync(int readerId, CancellationToken cancellationToken = default);
}
