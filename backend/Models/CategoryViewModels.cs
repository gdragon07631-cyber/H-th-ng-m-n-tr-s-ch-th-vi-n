using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public sealed class CategoryInputViewModel
{
    private int? _parentId;
    [Required(ErrorMessage = "Vui lòng nhập tên thể loại.")]
    [Display(Name = "Tên thể loại")]
    [MaxLength(150, ErrorMessage = "Tên thể loại không được vượt quá 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Thể loại cha")]
    public int? ParentId
    {
        get => _parentId;
        set { _parentId = value; ParentIdSpecified = true; }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool ParentIdSpecified { get; private set; }
}

public sealed class CategoryManagementViewModel
{
    public IReadOnlyList<Category> Categories { get; set; } = [];
    public IReadOnlyList<Category> ParentOptions { get; set; } = [];
}

public sealed class CategoryTreeViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<CategoryTreeViewModel> Children { get; set; } = [];
}

public sealed class CatalogBookCategoryOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
