namespace Project.Models;

public sealed class ReaderPasswordResetRequest
{
    public long Id { get; set; }
    public string EmailHash { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
}
