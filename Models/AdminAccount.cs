using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public static class AccountRoles
{
    public const string Librarian = "Librarian";
    public const string LibraryManager = "LibraryManager";
    public const string SystemAdmin = "SystemAdmin";
}

public sealed class AdminAccount
{
    public int Id { get; set; }

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(512)]
    public string PasswordHash { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string Role { get; set; } = AccountRoles.SystemAdmin;

    public bool IsActive { get; set; } = true;

    public int FailedLoginAttempts { get; set; }

    public DateTime? LockoutStartUtc { get; set; }

    public DateTime? LockoutEndUtc { get; set; }
}
