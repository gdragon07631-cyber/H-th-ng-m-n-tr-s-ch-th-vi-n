using System.Globalization;

namespace Project.Models;

/// <summary>Thông tin chỉ đọc của bản sao trên trang chi tiết đầu sách.</summary>
public sealed record BookCopyDetailsItem(
    long Id, string CopyCode, string Warehouse, string Shelf,
    DateOnly? ReceivedDate, decimal? CoverPrice, string PhysicalCondition, string Status)
{
    // Giữ nguyên giá trị trạng thái nghiệp vụ cũ, chỉ dùng nhãn đầy đủ tại bảng mới.
    public string StatusText => Status == BookCopyStatus.OnHold ? "Đang giữ cho đặt trước" : Status;
    public string ReceivedDateText => ReceivedDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Chưa có";
    public string CoverPriceText => CoverPrice.HasValue
        ? CoverPrice.Value.ToString("N2", CultureInfo.GetCultureInfo("vi-VN")) + " đ"
        : "Chưa có";
}
