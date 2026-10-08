using Project.Models;

namespace Project.Services;

public interface IHoldPickupService
{
    /// <summary>
    /// Lấy danh sách các đơn đặt giữ sách đang ở trạng thái chờ người đến nhận (sẵn sàng có bản sao),
    /// sắp xếp theo hạn nhận gần nhất trước (ascending).
    /// </summary>
    Task<IReadOnlyList<HoldPickupItemViewModel>> GetWaitingPickupHoldsAsync(CancellationToken cancellationToken = default);
}

