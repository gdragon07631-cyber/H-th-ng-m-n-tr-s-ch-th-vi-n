using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public static class AuditActions
{
    public const string Login = "Đăng nhập";
    public const string CreateAccount = "Tạo tài khoản";
    public const string UpdateAccount = "Sửa tài khoản";
    public const string IssueCard = "Cấp thẻ";
    public const string UpdateLoanPolicy = "Sửa chính sách mượn";

    public static readonly IReadOnlyList<string> All = [Login, CreateAccount, UpdateAccount, IssueCard, UpdateLoanPolicy];
}

/// <summary>Nhật ký hoạt động: ai đã thao tác gì, lúc nào, trên đối tượng nào và từ IP nào.</summary>
public sealed class AuditLog
{
    public long Id { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    [Required, MaxLength(256)]
    public string Actor { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Target { get; set; } = string.Empty;

    [Required, MaxLength(45)]
    public string IpAddress { get; set; } = string.Empty;
}
