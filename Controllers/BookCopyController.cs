using Microsoft.AspNetCore.Mvc;
using Project.Filters;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

/// <summary>Thủ thư xem, thêm và sửa bản sao của một đầu sách (kho, kệ, tình trạng, ghi chú, trạng thái).</summary>
[StaffOnly(AccountRoles.Librarian, AccountRoles.SystemAdmin, AccountRoles.LibraryManager)]
public sealed class BookCopyController(
    IBookCopyService bookCopyService,
    IAuditLogService auditLogService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int bookId, CancellationToken cancellationToken = default)
    {
        var model = await bookCopyService.GetBookCopiesAsync(bookId, cancellationToken);
        return model is null ? NotFound("Không tìm thấy đầu sách.") : View(model);
    }

    [HttpGet]
    public async Task<IActionResult> PreviewBatch(int quantity, CancellationToken cancellationToken = default)
    {
        var result = await bookCopyService.PreviewBatchAsync(quantity, cancellationToken);
        return result.IsSuccess ? Ok(result) : BadRequest(result);
    }

    [HttpGet]
    public async Task<IActionResult> PrintLabels(int bookId, [FromQuery] long[] copyIds, CancellationToken cancellationToken = default)
    {
        var model = await bookCopyService.GetLabelsForCopiesAsync(bookId, copyIds, cancellationToken);
        return model is null ? NotFound("Không tìm thấy danh sách bản sao cần in.") : View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int bookId, [Bind(Prefix = "NewCopy")] NewBookCopyViewModel model, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = string.Join(" ", ModelState.Values.SelectMany(state => state.Errors).Select(error => error.ErrorMessage));
            return RedirectToAction(nameof(Index), new { bookId });
        }

        var result = await bookCopyService.AddAsync(bookId, model, cancellationToken);
        if (result.Status == BookCopyUpdateStatus.NotFound) return NotFound(result.ErrorMessage);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? $"Đã thêm bản sao {result.Copy!.CopyCode}."
            : result.ErrorMessage;
        return RedirectToAction(nameof(Index), new { bookId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBatch(int bookId, [Bind(Prefix = "Batch")] NewBookCopyBatchViewModel batch, CancellationToken cancellationToken = default)
    {
        var page = await bookCopyService.GetBookCopiesAsync(bookId, cancellationToken);
        if (page is null) return NotFound("Không tìm thấy đầu sách.");
        page.Batch = batch;
        if (!ModelState.IsValid) return View("Index", page);

        var result = await bookCopyService.AddBatchAsync(bookId, batch, cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể tạo lô bản sao.");
            return View("Index", page);
        }

        page.CreatedCopies = result.Copies ?? [];
        page.SkippedBarcodes = result.SkippedBarcodes ?? [];
        page.BatchSuccessMessage = $"Tạo lô bản sao thành công: {page.CreatedCopies.Count} bản sao.";
        return View("Index", page);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken = default)
    {
        var model = await bookCopyService.GetForEditAsync(id, cancellationToken);
        if (model is null) return NotFound("Không tìm thấy bản sao.");
        await FillListsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(long id, BookCopyEditViewModel model, CancellationToken cancellationToken = default)
    {
        var current = await bookCopyService.GetForEditAsync(id, cancellationToken);
        if (current is null) return NotFound("Không tìm thấy bản sao.");

        // Mã vạch, đầu sách và trạng thái hiện tại luôn lấy từ dữ liệu đã lưu, không lấy từ form.
        model.Id = id;
        model.CopyCode = current.CopyCode;
        model.BookId = current.BookId;
        model.BookTitle = current.BookTitle;
        model.CurrentStatus = current.CurrentStatus;
        if (current.StatusLocked) model.Status = current.CurrentStatus;

        if (ModelState.IsValid)
        {
            var staff = await auditLogService.GetSignedInStaffAsync(Request, cancellationToken);
            var result = await bookCopyService.UpdateAsync(id, model, staff?.Email ?? "Không xác định", cancellationToken);
            if (result.IsSuccess)
            {
                TempData["SuccessMessage"] = $"Đã cập nhật bản sao {current.CopyCode}.";
                return RedirectToAction(nameof(Index), new { bookId = current.BookId });
            }

            var field = result.Status switch
            {
                BookCopyUpdateStatus.InvalidShelf => nameof(model.ShelfId),
                BookCopyUpdateStatus.InvalidCondition => nameof(model.PhysicalCondition),
                BookCopyUpdateStatus.ReasonRequired => nameof(model.Reason),
                BookCopyUpdateStatus.OnActiveLoan or BookCopyUpdateStatus.StatusManagedByHold or BookCopyUpdateStatus.InvalidStatus => nameof(model.Status),
                _ => string.Empty
            };
            ModelState.AddModelError(field, result.ErrorMessage ?? "Không thể lưu bản sao.");
        }

        await FillListsAsync(model, cancellationToken);
        return View(model);
    }

    private async Task FillListsAsync(BookCopyEditViewModel model, CancellationToken cancellationToken)
    {
        model.Warehouses = await bookCopyService.GetActiveWarehousesAsync(cancellationToken);
        model.Shelves = await bookCopyService.GetActiveShelvesAsync(cancellationToken);
        model.History = await bookCopyService.GetHistoryAsync(model.Id, cancellationToken);
    }
}
