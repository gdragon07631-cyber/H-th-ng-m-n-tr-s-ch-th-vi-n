using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Models;

public sealed class LibraryCardType
{
    public const int DefaultMaxRenewals = 3;
    public const int DefaultMaxBooks = 5;

    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public int MaxRenewals { get; set; } = DefaultMaxRenewals;

    [NotMapped]
    private int? _maxBooks;

    [NotMapped]
    public int MaxBooks
    {
        get => _maxBooks ?? GetDefaultLimitForCardType(Name);
        set => _maxBooks = value;
    }

    public static int GetDefaultLimitForCardType(string? cardTypeName)
    {
        return cardTypeName switch
        {
            "Thẻ cán bộ" => 10,
            "Thẻ giảng viên" => 10,
            _ => DefaultMaxBooks
        };
    }

    public ICollection<LibraryCard> LibraryCards { get; set; } = [];
}
