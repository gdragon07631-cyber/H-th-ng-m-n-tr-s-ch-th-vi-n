using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Project.Models;

public sealed class ManualBookCopyViewModel
{
    [Display(Name = "Cách nhập mã vạch")]
    public bool GenerateBarcode { get; set; }

    [ManualBarcodeRequired, MaxLength(50)]
    [Display(Name = "Mã vạch")]
    public string CopyCode { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn kho.")]
    [Display(Name = "Kho")]
    public int WarehouseId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn kệ.")]
    [Display(Name = "Kệ")]
    public int ShelfId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ngày nhập.")]
    [Display(Name = "Ngày nhập")]
    public DateOnly? ReceivedDate { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập giá bìa.")]
    [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "Giá bìa phải từ 0 đến 9999999999999999,99.")]
    [Display(Name = "Giá bìa")]
    public decimal? CoverPrice { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn tình trạng vật lý.")]
    [Display(Name = "Tình trạng vật lý")]
    public string PhysicalCondition { get; set; } = string.Empty;

    [BindNever] public int BookId { get; set; }
    [BindNever] public string BookTitle { get; set; } = string.Empty;
    [BindNever] public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
    [BindNever] public IReadOnlyList<Shelf> Shelves { get; set; } = [];
}

public sealed class ManualBarcodeRequiredAttribute : RequiredAttribute
{
    public ManualBarcodeRequiredAttribute() => ErrorMessage = "Vui lòng nhập mã vạch.";

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        validationContext.ObjectInstance is ManualBookCopyViewModel { GenerateBarcode: true }
            ? ValidationResult.Success : base.IsValid(value, validationContext);
}
