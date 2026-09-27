using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public sealed class LibraryCardType
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public ICollection<LibraryCard> LibraryCards { get; set; } = [];
}
