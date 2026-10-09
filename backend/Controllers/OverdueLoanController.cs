using Microsoft.AspNetCore.Mvc;
using Project.Filters;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

/// <summary>Danh sách chỉ đọc để thủ thư ưu tiên nhắc các phiếu mượn trễ lâu nhất.</summary>
[StaffOnly(AccountRoles.Librarian, AccountRoles.SystemAdmin, AccountRoles.LibraryManager)]
public sealed class OverdueLoanController(IBookLoanService loans) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct = default) =>
        View(new OverdueLoanViewModel
        {
            Items = await loans.GetOverdueAsync(DateOnly.FromDateTime(DateTime.Today), ct)
        });

    [HttpGet("api/overdue-loans")]
    public async Task<IActionResult> GetOverdueApi(CancellationToken ct = default)
    {
        var items = await loans.GetOverdueAsync(DateOnly.FromDateTime(DateTime.Today), ct);
        return Ok(items.Select(item => new
        {
            loanId = item.LoanId,
            daysOverdue = item.DaysOverdue,
            readerName = item.ReaderName,
            phoneNumber = item.PhoneNumber,
            cardCode = item.CardCode,
            bookTitle = item.BookTitle,
            loanDate = item.LoanDate,
            dueDate = item.DueDate,
            copyBarcode = item.CopyBarcode
        }));
    }
}
