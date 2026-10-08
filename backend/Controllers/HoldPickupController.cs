using Microsoft.AspNetCore.Mvc;
using Project.Filters;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

/// <summary>
/// Màn hình thủ thư: danh sách sách đang chờ người đến nhận (giá chờ).
/// Read-only: hiển thị mã vạch bản sao, tên bạn đọc, hạn nhận (sort theo hạn gần nhất).
/// </summary>
[StaffOnly(AccountRoles.Librarian, AccountRoles.SystemAdmin, AccountRoles.LibraryManager)]
public sealed class HoldPickupController(IHoldPickupService holdPickupService) : Controller
{
    // ==========================================
    // MVC VIEW ACTION
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct = default)
    {
        var items = await holdPickupService.GetWaitingPickupHoldsAsync(ct);
        return View(new HoldPickupListViewModel
        {
            Items = items
        });
    }

    // ==========================================
    // REST API ENDPOINT
    // ==========================================

    [HttpGet("api/hold-pickups")]
    public async Task<IActionResult> GetAllApi(CancellationToken ct = default)
    {
        var items = await holdPickupService.GetWaitingPickupHoldsAsync(ct);
        return Ok(items.Select(h => new
        {
            holdId = h.HoldId,
            bookId = h.BookId,
            bookTitle = h.BookTitle,
            isbn = h.Isbn,
            bookCopyId = h.BookCopyId,
            copyBarcode = h.CopyBarcode,
            readerAccountId = h.ReaderAccountId,
            readerName = h.ReaderName,
            readerEmail = h.ReaderEmail,
            readerPhone = h.ReaderPhone,
            pickupDeadlineUtc = h.PickupDeadlineUtc,
            heldAtUtc = h.HeldAtUtc,
            status = h.Status,
            currentShelf = h.CurrentShelf,
            currentWarehouse = h.CurrentWarehouse
        }));
    }
}

