using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public sealed class BookCopy
{
    public long Id { get; set; }
    public int BookId { get; set; }
    public Book? Book { get; set; }
    public int ShelfId { get; set; }
    public Shelf? Shelf { get; set; }

    [Required, MaxLength(50)]
    public string CopyCode { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Status { get; set; } = "Sẵn sàng";
}
