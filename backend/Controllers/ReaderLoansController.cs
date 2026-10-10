using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

public sealed class ReaderLoansController(
    ApplicationDbContext db,
    IDataProtectionProvider dataProtectionProvider,
    IBookLoanService loans) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct = default)
    {
        var readerId = await GetReaderIdAsync(ct);
        if (readerId <= 0) return RedirectToAction("Login", "ReaderRegistration", new { returnUrl = Url.Action(nameof(Index)) });
        return View(await loans.GetForReaderAsync(readerId, ct));
    }

    [HttpGet("api/reader/loans")]
    public async Task<IActionResult> GetMine(CancellationToken ct = default)
    {
        var readerId = await GetReaderIdAsync(ct);
        if (readerId <= 0) return Unauthorized(new { message = "Vui lòng đăng nhập trước khi xem phiếu mượn." });
        var today = DateOnly.FromDateTime(DateTime.Today);
        var items = await loans.GetForReaderAsync(readerId, ct);
        return Ok(items.Select(loan => new
        {
            id = loan.Id, bookId = loan.BookId, bookTitle = loan.Book?.Title,
            loan.LoanDate, loan.DueDate, status = "Đang mượn",
            renewalCount = loan.RenewalCount,
            renewalLimit = loan.ReaderAccount?.LibraryCard?.LibraryCardType?.MaxRenewals ?? 0,
            canRenew = loan.DueDate >= today && loan.RenewalCount < (loan.ReaderAccount?.LibraryCard?.LibraryCardType?.MaxRenewals ?? 0)
        }));
    }

    [HttpPost("api/reader/loans/{id:long}/renew"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Renew(long id, CancellationToken ct = default)
    {
        var readerId = await GetReaderIdAsync(ct);
        if (readerId <= 0) return Unauthorized(new { success = false, message = "Vui lòng đăng nhập trước khi gia hạn phiếu mượn." });
        var result = await loans.RenewForReaderAsync(id, readerId, DateOnly.FromDateTime(DateTime.Today), ct);
        if (!result.IsSuccess) return BadRequest(new { success = false, message = result.ErrorMessage });
        var limit = result.Loan!.ReaderAccount?.LibraryCard?.LibraryCardType?.MaxRenewals ?? 0;
        return Ok(new
        {
            success = true, message = "Gia hạn phiếu mượn thành công.",
            data = new { id, newDueDate = result.Loan.DueDate, renewalCount = result.Loan.RenewalCount, renewalLimit = limit }
        });
    }

    private Task<int> GetReaderIdAsync(CancellationToken ct) => ReaderSessionCookies.GetReaderIdAsync(
        HttpContext, dataProtectionProvider,
        (id, token) => db.ReaderAccounts.AsNoTracking().SingleOrDefaultAsync(reader => reader.Id == id, token), ct);
}
