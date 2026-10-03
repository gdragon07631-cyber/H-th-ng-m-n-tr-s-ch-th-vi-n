using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Project.Models;

public sealed class Book
{
    public int Id { get; set; }

    [Required, MaxLength(250)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(250)]
    public string? Subtitle { get; set; }

    [MaxLength(50)]
    public string? Isbn { get; set; }

    [Required]
    public int AuthorId { get; set; }

    [JsonIgnore]
    public Author? Author { get; set; }

    /// <summary>Tất cả tác giả của đầu sách; AuthorId luôn là tác giả đầu tiên trong danh sách này.</summary>
    [JsonIgnore]
    public ICollection<BookAuthor> BookAuthors { get; set; } = [];

    public int? CategoryId { get; set; }

    [JsonIgnore]
    public Category? Category { get; set; }

    [MaxLength(200)]
    public string? Publisher { get; set; }

    public int? PublicationYear { get; set; }

    public int? PageCount { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? CoverImagePath { get; set; }

    [MaxLength(500)]
    public string? ThumbnailImagePath { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
