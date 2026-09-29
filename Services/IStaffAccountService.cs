using Project.Models;

namespace Project.Services;

public enum StaffAccountStatus
{
    Success,
    NotFound,
    DuplicateEmail,
    InvalidRole,
    CannotChangeOwnAccess,
    PasswordAlreadySet
}

public sealed record StaffAccountResult(StaffAccountStatus Status, AdminAccount? Account = null, string? ErrorMessage = null)
{
    public bool IsSuccess => Status == StaffAccountStatus.Success;

    /// <summary>Email đặt mật khẩu đã được gửi thành công (chỉ có nghĩa khi có gửi email).</summary>
    public bool EmailSent { get; init; }

    /// <summary>Liên kết đặt mật khẩu vừa tạo; chỉ hiển thị ở môi trường phát triển khi chưa cấu hình SMTP.</summary>
    public string? SetupLink { get; init; }
}

public interface IStaffAccountService
{
    Task<IReadOnlyList<AdminAccount>> ListAsync(CancellationToken cancellationToken = default);
    Task<AdminAccount?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Tạo tài khoản chưa có mật khẩu và gửi email đặt mật khẩu lần đầu (hết hạn sau 24 giờ).</summary>
    Task<StaffAccountResult> CreateAsync(StaffAccountFormViewModel model, string setupUrl, CancellationToken cancellationToken = default);

    Task<StaffAccountResult> UpdateAsync(int id, StaffAccountFormViewModel model, int actingAdminId, CancellationToken cancellationToken = default);

    /// <summary>Khoá/mở khoá. Khoá sẽ thu hồi mọi phiên đăng nhập của tài khoản ngay lập tức.</summary>
    Task<StaffAccountResult> SetActiveAsync(int id, bool isActive, int actingAdminId, CancellationToken cancellationToken = default);

    Task<StaffAccountResult> ResendSetupEmailAsync(int id, string setupUrl, CancellationToken cancellationToken = default);

    Task<bool> IsSetupTokenValidAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Đặt mật khẩu lần đầu bằng liên kết hợp lệ; trả về tài khoản khi thành công.</summary>
    Task<AdminAccount?> CompleteSetupAsync(string token, string password, CancellationToken cancellationToken = default);
}
