using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public static class AuthorStatus
{
    public const string Active = "Hoạt động";
    public const string Inactive = "Ngừng sử dụng";
}

public sealed class Author
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Note { get; set; }

    [Required, MaxLength(50)]
    public string Status { get; set; } = AuthorStatus.Active;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
