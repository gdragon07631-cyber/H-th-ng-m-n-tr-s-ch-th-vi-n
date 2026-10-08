using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public sealed class ReaderRegistrationViewModel
{
    [Required(ErrorMessage = "Họ và tên không được để trống.")]
    [MaxLength(100, ErrorMessage = "Họ và tên không được vượt quá 100 ký tự.")]
    [Display(Name = "Họ và tên")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ngày sinh không được để trống.")]
    [DataType(DataType.Date)]
    [NotFutureDate(ErrorMessage = "Ngày sinh không hợp lệ.")]
    [Display(Name = "Ngày sinh")]
    public DateOnly? DateOfBirth { get; set; }

    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [MaxLength(256, ErrorMessage = "Email không được vượt quá 256 ký tự.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại không được để trống.")]
    [MaxLength(20, ErrorMessage = "Số điện thoại không được vượt quá 20 ký tự.")]
    [RegularExpression(@"0[0-9]{9}", ErrorMessage = "Số điện thoại không hợp lệ.")]
    [Display(Name = "Số điện thoại")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mã sinh viên hoặc mã cán bộ không được để trống.")]
    [MaxLength(50, ErrorMessage = "Mã sinh viên hoặc mã cán bộ không được vượt quá 50 ký tự.")]
    [Display(Name = "Mã sinh viên hoặc mã cán bộ")]
    public string StudentOrStaffCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu không được để trống.")]
    [DataType(DataType.Password)]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).{8,}$", ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự, bao gồm chữ và số.")]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhập lại mật khẩu không được để trống.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Mật khẩu nhập lại không khớp.")]
    [Display(Name = "Nhập lại mật khẩu")]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>Loại thẻ bạn đọc chọn; thẻ được cấp tự động khi email được xác nhận.</summary>
    [Required(ErrorMessage = "Vui lòng chọn loại thẻ thư viện.")]
    [Display(Name = "Loại thẻ thư viện")]
    public int? LibraryCardTypeId { get; set; }

    public IReadOnlyList<LibraryCardType> CardTypes { get; set; } = [];
}
