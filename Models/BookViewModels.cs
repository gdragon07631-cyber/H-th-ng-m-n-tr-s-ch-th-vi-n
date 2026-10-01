using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public sealed class CatalogBookViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập tên sách.")]
    [Display(Name = "Tên sách")]
    [MaxLength(250, ErrorMessage = "Tên sách không được vượt quá 250 ký tự.")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Mã ISBN")]
    [MaxLength(50, ErrorMessage = "Mã ISBN không được vượt quá 50 ký tự.")]
    public string? Isbn { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn tác giả.")]
    [Display(Name = "Tác giả")]
    public int AuthorId { get; set; }

    [Display(Name = "Mô tả / Tóm tắt")]
    [MaxLength(500, ErrorMessage = "Mô tả không được vượt quá 500 ký tự.")]
    public string? Description { get; set; }

    public IReadOnlyList<Author> ActiveAuthors { get; set; } = [];

    [Required(ErrorMessage = "Vui lòng chọn thể loại.")]
    [Display(Name = "Thể loại")]
    public int CategoryId { get; set; }

    public IReadOnlyList<Category> ActiveCategories { get; set; } = [];
}

public sealed class BookDetailsViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Isbn { get; set; }
    public int AuthorId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorStatus { get; set; } = string.Empty;
    public string CategoryName { get; set; } = "Chưa phân loại";
    public string? Description { get; set; }
    public string? CoverImagePath { get; set; }
    public string? ThumbnailImagePath { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool IsReaderSignedIn { get; set; }
    public bool CanHold { get; set; }
    public bool IsLibrarian { get; set; }
    public int AvailableCopies { get; set; }
    public int TotalCopies { get; set; }
}
