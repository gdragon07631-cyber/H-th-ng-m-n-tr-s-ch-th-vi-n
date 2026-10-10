using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

public sealed class ReaderLoanController(
    ApplicationDbContext db,
    IBookLoanService loans,
    IDataProtectionProvider dataProtectionProvider) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct = default)
    {
        var readerId = await GetReaderIdAsync(ct);
        if (readerId <= 0) return RedirectToAction("Login", "ReaderRegistration");

        var readerLoans = await db.BookLoans.AsNoTracking()
            .Where(loan => loan.ReaderAccountId == readerId)
            .Include(loan => loan.Book)
            .Include(loan => loan.ReaderAccount).ThenInclude(reader => reader!.LibraryCard)
                .ThenInclude(card => card!.LibraryCardType)
            .OrderByDescending(loan => loan.CreatedAtUtc)
            .ToListAsync(ct);
        return View(readerLoans);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Renew(long id, CancellationToken ct = default)
    {
        var readerId = await GetReaderIdAsync(ct);
        if (readerId <= 0) return RedirectToAction("Login", "ReaderRegistration");

        var belongsToReader = await db.BookLoans.AsNoTracking()
            .AnyAsync(loan => loan.Id == id && loan.ReaderAccountId == readerId, ct);
        if (!belongsToReader) return NotFound();

        var result = await loans.RenewAsync(id, DateOnly.FromDateTime(DateTime.Today), ct);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? $"Gia hạn thành công. Hạn trả mới: {result.Loan!.DueDate:dd/MM/yyyy}. Số lần gia hạn: {result.Loan.RenewalCount} / {result.Loan.ReaderAccount!.LibraryCard!.LibraryCardType!.MaxRenewals}."
            : result.ErrorMessage;
        return RedirectToAction(nameof(Index));
    }

    private Task<int> GetReaderIdAsync(CancellationToken ct) =>
        ReaderSessionCookies.GetReaderIdAsync(HttpContext, dataProtectionProvider,
            (id, token) => db.ReaderAccounts.SingleOrDefaultAsync(reader => reader.Id == id, token), ct);
}
