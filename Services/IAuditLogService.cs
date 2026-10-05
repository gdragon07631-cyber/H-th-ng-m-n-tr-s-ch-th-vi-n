using Project.Models;

namespace Project.Services;

public interface IAuditLogService
{
    /// <summary>Ghi một bản ghi nhật ký. Lỗi ghi nhật ký không làm hỏng thao tác nghiệp vụ đã thành công.</summary>
    Task WriteAsync(string actor, string action, string target, string? ipAddress, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditLog>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Nhật ký thỏa mãn đồng thời mọi điều kiện của bộ lọc, mới nhất trước. Ngày lọc tính theo giờ địa phương.</summary>
    Task<IReadOnlyList<AuditLog>> SearchAsync(AuditLogFilter filter, int limit, CancellationToken cancellationToken = default);

    /// <summary>Danh sách người thực hiện đã có trong nhật ký, dùng cho ô chọn của bộ lọc.</summary>
    Task<IReadOnlyList<string>> GetActorsAsync(CancellationToken cancellationToken = default);

    /// <summary>Tài khoản nhân viên (quản trị/thủ thư) đang đăng nhập theo cookie admin_refresh, nếu có.</summary>
    Task<AdminAccount?> GetSignedInStaffAsync(HttpRequest request, CancellationToken cancellationToken = default);
}
