using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public sealed class RefreshToken
{
    public long Id { get; set; }

    public int AdminAccountId { get; set; }

    public AdminAccount AdminAccount { get; set; } = null!;

    [Required, MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    [MaxLength(64)]
    public string? ReplacedByTokenHash { get; set; }
}
