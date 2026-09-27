using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public sealed class ReaderAccount
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    public DateOnly DateOfBirth { get; set; }

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string StudentOrStaffCode { get; set; } = string.Empty;

    [Required, MaxLength(512)]
    public string PasswordHash { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string Status { get; set; } = "Chờ duyệt";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public int SessionVersion { get; set; }
}
