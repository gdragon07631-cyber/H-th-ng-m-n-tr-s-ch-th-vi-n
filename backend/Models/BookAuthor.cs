using System.Text.Json.Serialization;

namespace Project.Models;

/// <summary>Bảng liên kết nhiều-nhiều giữa đầu sách và tác giả trong danh mục.</summary>
public sealed class BookAuthor
{
    public int BookId { get; set; }

    [JsonIgnore]
    public Book? Book { get; set; }

    public int AuthorId { get; set; }

    [JsonIgnore]
    public Author? Author { get; set; }

    /// <summary>Thứ tự tác giả theo lựa chọn của thủ thư; tác giả thứ tự 0 là tác giả chính (Book.AuthorId).</summary>
    public int SortOrder { get; set; }
}
