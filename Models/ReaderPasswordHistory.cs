using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public sealed class ReaderPasswordHistory
{
    public int Id { get; set; }

    public int ReaderAccountId { get; set; }
    public ReaderAccount? ReaderAccount { get; set; }

    [Required, MaxLength(512)]
    public string PasswordHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
