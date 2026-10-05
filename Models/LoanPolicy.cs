using System.ComponentModel.DataAnnotations;

namespace Project.Models;

/// <summary>Chính sách mượn dùng chung của thư viện (một bản ghi duy nhất).</summary>
public sealed class LoanPolicy
{
    public const int SingletonId = 1;
    public const int DefaultLoanDays = 14;

    public int Id { get; set; }

    public int LoanDays { get; set; } = DefaultLoanDays;

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class LoanPolicyViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập số ngày mượn.")]
    [Range(1, 365, ErrorMessage = "Số ngày mượn phải từ 1 đến 365.")]
    [Display(Name = "Số ngày mượn tối đa")]
    public int LoanDays { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
}
