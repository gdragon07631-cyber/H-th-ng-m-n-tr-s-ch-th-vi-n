using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public sealed class BookCopyIndexViewModel
{
    public int BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public IReadOnlyList<BookCopy> Copies { get; set; } = [];
    public IReadOnlyList<BookCopy> CreatedCopies { get; set; } = [];
    public IReadOnlyList<string> SkippedBarcodes { get; set; } = [];
    public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
    public IReadOnlyList<Shelf> Shelves { get; set; } = [];
    public NewBookCopyViewModel NewCopy { get; set; } = new();
    public NewBookCopyBatchViewModel Batch { get; set; } = new();
    public string? BatchSuccessMessage { get; set; }
}

public sealed class NewBookCopyBatchViewModel
{
    [Range(1, 50, ErrorMessage = "Số lượng phải là số nguyên từ 1 đến 50.")]
    [Display(Name = "Số lượng")]
    public int Quantity { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn kho.")]
    [Display(Name = "Kho")]
    public int WarehouseId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn kệ.")]
    [Display(Name = "Kệ")]
    public int ShelfId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ngày nhập.")]
    [Display(Name = "Ngày nhập")]
    public DateOnly? ReceivedDate { get; set; }
}

public sealed class NewBookCopyViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập mã vạch.")]
    [MaxLength(50, ErrorMessage = "Mã vạch không được vượt quá 50 ký tự.")]
    [Display(Name = "Mã vạch")]
    public string CopyCode { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn kệ.")]
    [Display(Name = "Kệ")]
    public int ShelfId { get; set; }

    [Display(Name = "Tình trạng vật lý")]
    public string PhysicalCondition { get; set; } = BookCopyCondition.Good;

    [MaxLength(500, ErrorMessage = "Ghi chú không được vượt quá 500 ký tự.")]
    [Display(Name = "Ghi chú")]
    public string? Note { get; set; }
}

public sealed class BookCopyEditViewModel
{
    public long Id { get; set; }

    /// <summary>Chỉ để hiển thị; mã vạch không sửa được.</summary>
    public string CopyCode { get; set; } = string.Empty;
    public int BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn kho.")]
    [Display(Name = "Kho")]
    public int WarehouseId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn kệ.")]
    [Display(Name = "Kệ")]
    public int ShelfId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn tình trạng vật lý.")]
    [Display(Name = "Tình trạng vật lý")]
    public string PhysicalCondition { get; set; } = BookCopyCondition.Good;

    [Required(ErrorMessage = "Vui lòng chọn trạng thái.")]
    [Display(Name = "Trạng thái")]
    public string Status { get; set; } = BookCopyStatus.Available;

    [MaxLength(500, ErrorMessage = "Ghi chú không được vượt quá 500 ký tự.")]
    [Display(Name = "Ghi chú")]
    public string? Note { get; set; }

    [MaxLength(500, ErrorMessage = "Lý do không được vượt quá 500 ký tự.")]
    [Display(Name = "Lý do đổi trạng thái")]
    public string? Reason { get; set; }

    public IReadOnlyList<Warehouse> Warehouses { get; set; } = [];
    public IReadOnlyList<Shelf> Shelves { get; set; } = [];
    public IReadOnlyList<BookCopyStatusHistory> History { get; set; } = [];

    /// <summary>Trạng thái do nghiệp vụ mượn/đặt giữ quản lý thì thủ thư không đổi trực tiếp.</summary>
    public bool StatusLocked => !BookCopyStatus.Editable.Contains(CurrentStatus);
}

public sealed class BookCopyLabelPrintOptions
{
    public string LibraryName { get; set; } = "Thư viện";
    public int WidthMm { get; set; } = 70;
    public int HeightMm { get; set; } = 38;
    public int GapMm { get; set; } = 4;
    public int Columns { get; set; } = 2;
    public int BarcodeWidthPx { get; set; } = 360;
    public int BarcodeHeightPx { get; set; } = 100;
}

public sealed class BookCopyLabelsViewModel
{
    public int BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public BookCopyLabelPrintOptions Layout { get; set; } = new();
    public IReadOnlyList<BookCopyLabelViewModel> Labels { get; set; } = [];
}

public sealed class BookCopyLabelViewModel
{
    public long CopyId { get; set; }
    public string CopyCode { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public string WarehouseCode { get; set; } = string.Empty;
    public string ShelfCode { get; set; } = string.Empty;
    public DateOnly? ReceivedDate { get; set; }
    public string BarcodeImageDataUri { get; set; } = string.Empty;
}
