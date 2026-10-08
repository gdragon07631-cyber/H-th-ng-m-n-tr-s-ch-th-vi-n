using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public sealed class LoginLog
{
    public long Id { get; set; }

    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(45)]
    public string? IpAddress { get; set; }

    public DateTime AttemptedAtUtc { get; set; }

    [Required, MaxLength(32)]
    public string Status { get; set; } = string.Empty;

    public int? AdminAccountId { get; set; }

    public AdminAccount? AdminAccount { get; set; }
}
