namespace Project.Models;

public sealed class ReaderPasswordResetToken
{
    public long Id { get; set; }
    public int ReaderAccountId { get; set; }
    public ReaderAccount ReaderAccount { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
