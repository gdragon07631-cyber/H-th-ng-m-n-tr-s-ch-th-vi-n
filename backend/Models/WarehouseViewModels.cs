using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public sealed class CreateWarehouseViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập mã kho.")]
    [Display(Name = "Mã kho")]
    [MaxLength(50, ErrorMessage = "Mã kho không được vượt quá 50 ký tự.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên kho.")]
    [Display(Name = "Tên kho")]
    [MaxLength(150, ErrorMessage = "Tên kho không được vượt quá 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Địa chỉ")]
    [MaxLength(500, ErrorMessage = "Địa chỉ không được vượt quá 500 ký tự.")]
    public string? Address { get; set; }

    [Display(Name = "Mô tả")]
    [MaxLength(500, ErrorMessage = "Mô tả không được vượt quá 500 ký tự.")]
    public string? Description { get; set; }
}

public sealed class EditWarehouseViewModel
{
    [Required]
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mã kho.")]
    [Display(Name = "Mã kho")]
    [MaxLength(50, ErrorMessage = "Mã kho không được vượt quá 50 ký tự.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên kho.")]
    [Display(Name = "Tên kho")]
    [MaxLength(150, ErrorMessage = "Tên kho không được vượt quá 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Địa chỉ")]
    [MaxLength(500, ErrorMessage = "Địa chỉ không được vượt quá 500 ký tự.")]
    public string? Address { get; set; }

    [Display(Name = "Mô tả")]
    [MaxLength(500, ErrorMessage = "Mô tả không được vượt quá 500 ký tự.")]
    public string? Description { get; set; }

    [Display(Name = "Trạng thái")]
    [MaxLength(50)]
    public string Status { get; set; } = WarehouseStatus.Active;
}

public sealed class WarehouseIndexViewModel
{
    public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
    public CreateWarehouseViewModel NewWarehouse { get; set; } = new();
}

public sealed class WarehouseDetailsViewModel
{
    public Warehouse Warehouse { get; set; } = null!;
    public IReadOnlyList<Shelf> Shelves { get; set; } = [];
    public CreateShelfViewModel NewShelf { get; set; } = new();
}

public sealed class CreateShelfViewModel
{
    [Required(ErrorMessage = "Vui lòng chọn kho trực thuộc.")]
    [Display(Name = "Kho trực thuộc")]
    public int WarehouseId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mã kệ.")]
    [Display(Name = "Mã kệ")]
    [MaxLength(50, ErrorMessage = "Mã kệ không được vượt quá 50 ký tự.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên kệ.")]
    [Display(Name = "Tên kệ")]
    [MaxLength(150, ErrorMessage = "Tên kệ không được vượt quá 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Mô tả")]
    [MaxLength(500, ErrorMessage = "Mô tả không được vượt quá 500 ký tự.")]
    public string? Description { get; set; }
}

public sealed class EditShelfViewModel
{
    [Required]
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn kho trực thuộc.")]
    [Display(Name = "Kho trực thuộc")]
    public int WarehouseId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mã kệ.")]
    [Display(Name = "Mã kệ")]
    [MaxLength(50, ErrorMessage = "Mã kệ không được vượt quá 50 ký tự.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên kệ.")]
    [Display(Name = "Tên kệ")]
    [MaxLength(150, ErrorMessage = "Tên kệ không được vượt quá 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Mô tả")]
    [MaxLength(500, ErrorMessage = "Mô tả không được vượt quá 500 ký tự.")]
    public string? Description { get; set; }

    [Display(Name = "Trạng thái")]
    [MaxLength(50)]
    public string Status { get; set; } = ShelfStatus.Active;
}

public sealed class ShelfIndexViewModel
{
    public IReadOnlyList<Shelf> Shelves { get; set; } = [];
    public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
    public int? SelectedWarehouseId { get; set; }
    public CreateShelfViewModel NewShelf { get; set; } = new();
}

public sealed class CreateWarehouseDto
{
    [Required(ErrorMessage = "Vui lòng nhập mã kho.")]
    [MaxLength(50, ErrorMessage = "Mã kho không được vượt quá 50 ký tự.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên kho.")]
    [MaxLength(150, ErrorMessage = "Tên kho không được vượt quá 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "Địa chỉ không được vượt quá 500 ký tự.")]
    public string? Address { get; set; }

    [MaxLength(500, ErrorMessage = "Mô tả không được vượt quá 500 ký tự.")]
    public string? Description { get; set; }
}

public sealed class UpdateWarehouseDto
{
    [Required(ErrorMessage = "Vui lòng nhập mã kho.")]
    [MaxLength(50, ErrorMessage = "Mã kho không được vượt quá 50 ký tự.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên kho.")]
    [MaxLength(150, ErrorMessage = "Tên kho không được vượt quá 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "Địa chỉ không được vượt quá 500 ký tự.")]
    public string? Address { get; set; }

    [MaxLength(500, ErrorMessage = "Mô tả không được vượt quá 500 ký tự.")]
    public string? Description { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; }
}

public sealed class CreateShelfDto
{
    [Required(ErrorMessage = "Vui lòng chọn kho trực thuộc.")]
    public int WarehouseId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mã kệ.")]
    [MaxLength(50, ErrorMessage = "Mã kệ không được vượt quá 50 ký tự.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên kệ.")]
    [MaxLength(150, ErrorMessage = "Tên kệ không được vượt quá 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "Mô tả không được vượt quá 500 ký tự.")]
    public string? Description { get; set; }
}

public sealed class UpdateShelfDto
{
    [Required(ErrorMessage = "Vui lòng chọn kho trực thuộc.")]
    public int WarehouseId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mã kệ.")]
    [MaxLength(50, ErrorMessage = "Mã kệ không được vượt quá 50 ký tự.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên kệ.")]
    [MaxLength(150, ErrorMessage = "Tên kệ không được vượt quá 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "Mô tả không được vượt quá 500 ký tự.")]
    public string? Description { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; }
}
