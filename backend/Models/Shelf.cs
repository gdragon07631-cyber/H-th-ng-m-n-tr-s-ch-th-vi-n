using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Project.Models;

public static class ShelfStatus
{
    public const string Active = "Hoạt động";
    public const string Inactive = "Ngừng sử dụng";
}

public sealed class Shelf
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required, MaxLength(50)]
    public string Status { get; set; } = ShelfStatus.Active;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [Required]
    public int WarehouseId { get; set; }

    [JsonIgnore]
    public Warehouse? Warehouse { get; set; }
}
