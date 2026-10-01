using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Project.Models;

public sealed class Book
{
    public int Id { get; set; }

    [Required, MaxLength(250)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Isbn { get; set; }

    [Required]
    public int AuthorId { get; set; }

    [JsonIgnore]
    public Author? Author { get; set; }

    public int? CategoryId { get; set; }

    [JsonIgnore]
    public Category? Category { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? CoverImagePath { get; set; }

    [MaxLength(500)]
    public string? ThumbnailImagePath { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
