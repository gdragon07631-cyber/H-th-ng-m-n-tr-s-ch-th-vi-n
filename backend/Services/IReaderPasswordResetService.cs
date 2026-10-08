namespace Project.Services;

public interface IReaderPasswordResetService
{
    Task<bool> RequestAsync(string email, string resetUrl, CancellationToken cancellationToken = default);
    Task<bool> IsTokenValidAsync(string token, CancellationToken cancellationToken = default);
    Task<bool> ResetAsync(string token, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>Mã tài khoản bạn đọc của liên kết đặt lại mật khẩu còn hiệu lực (để ghi nhật ký); null nếu liên kết không hợp lệ.</summary>
    Task<int?> GetReaderIdForTokenAsync(string token, CancellationToken cancellationToken = default);
}
