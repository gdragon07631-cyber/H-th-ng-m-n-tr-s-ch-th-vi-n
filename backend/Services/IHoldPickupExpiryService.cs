namespace Project.Services;

public interface IHoldPickupExpiryService
{
    /// <summary>
    /// Hủy các đơn đặt giữ đã có sách nhưng quá hạn nhận, trả bản sao về kệ và chuyển cho người kế tiếp trong hàng đợi.
    /// Trả về số đơn đã hủy.
    /// </summary>
    Task<int> ExpireOverdueAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
