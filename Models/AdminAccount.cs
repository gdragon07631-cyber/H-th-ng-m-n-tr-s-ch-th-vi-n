using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public static class AccountRoles
{
    public const string Librarian = "Librarian";
    public const string LibraryManager = "LibraryManager";
    public const string SystemAdmin = "SystemAdmin";

    /// <summary>Ba vai trò hợp lệ; mỗi tài khoản nhận đúng một vai trò.</summary>
    public static readonly IReadOnlyList<string> All = [Librarian, LibraryManager, SystemAdmin];

    public static bool IsValid(string? role) => role is not null && All.Contains(role);

    public static string DisplayName(string role) => role switch
    {
        Librarian => "Thủ thư",
        LibraryManager => "Quản lý thư viện",
        SystemAdmin => "Quản trị hệ thống",
        _ => role
    };
}

public sealed class AdminAccount
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    [Required, MaxLength(512)]
    public string PasswordHash { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string Role { get; set; } = AccountRoles.SystemAdmin;

    public bool IsActive { get; set; } = true;

    public int FailedLoginAttempts { get; set; }

    public DateTime? LockoutStartUtc { get; set; }

    public DateTime? LockoutEndUtc { get; set; }
}
