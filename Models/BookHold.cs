namespace Project.Models;

public sealed class BookHold
{
    public long Id { get; set; }
    public int ReaderAccountId { get; set; }
    public ReaderAccount? ReaderAccount { get; set; }
    public int BookId { get; set; }
    public Book? Book { get; set; }
    public DateTime HeldAtUtc { get; set; } = DateTime.UtcNow;
}
