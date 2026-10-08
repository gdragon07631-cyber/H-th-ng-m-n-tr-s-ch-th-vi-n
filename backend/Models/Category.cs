using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Project.Models;

public static class CategoryStatus
{
    public const string Active = "Hoạt động";
    public const string Inactive = "Ngừng sử dụng";
}

public sealed class Category
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string Status { get; set; } = CategoryStatus.Active;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public int? ParentId { get; set; }

    [JsonIgnore]
    public Category? Parent { get; set; }

    [JsonIgnore]
    public ICollection<Category> Children { get; set; } = [];

    [JsonIgnore]
    public ICollection<Book> Books { get; set; } = [];
}
