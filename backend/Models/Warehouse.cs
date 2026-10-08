using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Project.Models;

public static class WarehouseStatus
{
    public const string Active = "Hoạt động";
    public const string Inactive = "Ngừng sử dụng";
}

public sealed class Warehouse
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required, MaxLength(50)]
    public string Status { get; set; } = WarehouseStatus.Active;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public ICollection<Shelf> Shelves { get; set; } = [];
}
