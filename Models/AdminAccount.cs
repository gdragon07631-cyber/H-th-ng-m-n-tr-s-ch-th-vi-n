using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public sealed class AdminAccount
{
    public int Id { get; set; }

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(512)]
    public string PasswordHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public int FailedLoginAttempts { get; set; }

    public DateTime? LockoutStartUtc { get; set; }

    public DateTime? LockoutEndUtc { get; set; }
}
