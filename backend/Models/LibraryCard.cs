using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Models;

public sealed class LibraryCard
{
    public int Id { get; set; }

    [Required, MaxLength(32)]
    public string CardCode { get; set; } = string.Empty;

    public int ReaderAccountId { get; set; }
    public ReaderAccount? ReaderAccount { get; set; }

    public int LibraryCardTypeId { get; set; }
    public LibraryCardType? LibraryCardType { get; set; }

    public DateOnly IssuedOn { get; set; }
    public DateOnly ExpiresOn { get; set; }

    [Required, MaxLength(50)]
    public string Status { get; set; } = "Đang hoạt động";

    [NotMapped]
    public bool IsLocked
    {
        get => _isLocked || (!string.IsNullOrEmpty(Status) && (Status.Contains("khóa", StringComparison.OrdinalIgnoreCase) || Status.Contains("khoá", StringComparison.OrdinalIgnoreCase)));
        set => _isLocked = value;
    }
    private bool _isLocked;
}
