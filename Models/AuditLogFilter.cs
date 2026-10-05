using System.ComponentModel.DataAnnotations;

namespace Project.Models;

/// <summary>Điều kiện tra cứu nhật ký hoạt động; các điều kiện được kết hợp với nhau (AND).</summary>
public sealed class AuditLogFilter
{
    [DataType(DataType.Date)]
    [Display(Name = "Từ ngày")]
    public DateOnly? FromDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Đến ngày")]
    public DateOnly? ToDate { get; set; }

    [Display(Name = "Người thực hiện")]
    public string? Actor { get; set; }

    [Display(Name = "Loại hành động")]
    public string? Action { get; set; }

    /// <summary>Tìm trong người thực hiện và đối tượng, ví dụ email bạn đọc, mã thẻ, tên sách.</summary>
    [Display(Name = "Từ khoá")]
    [MaxLength(200)]
    public string? Keyword { get; set; }

    public bool IsEmpty =>
        FromDate is null && ToDate is null && string.IsNullOrWhiteSpace(Actor) && string.IsNullOrWhiteSpace(Action) &&
        string.IsNullOrWhiteSpace(Keyword);
}

public sealed class AuditLogIndexViewModel
{
    public AuditLogFilter Filter { get; set; } = new();
    public IReadOnlyList<AuditLog> Logs { get; set; } = [];
    public IReadOnlyList<string> Actors { get; set; } = [];
    public IReadOnlyList<string> Actions { get; set; } = AuditActions.All;
    public string? ErrorMessage { get; set; }
}
