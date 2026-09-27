using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

public sealed class LoanController(IBookLoanService loans, ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct = default) => View(await BuildModel(ct));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateBookLoanViewModel model, CancellationToken ct = default)
    {
        if (!ModelState.IsValid || model.LoanDate == null)
        {
            TempData["ErrorMessage"] = ModelState.Values.SelectMany(value => value.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu phiếu mượn không hợp lệ.";
            return View(nameof(Index), await BuildModel(ct));
        }

        var result = await loans.CreateAsync(model.BookId, model.ReaderAccountId, model.LoanDate.Value, ct);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? $"Đã tạo phiếu mượn. Hạn trả: {result.Loan!.DueDate:dd/MM/yyyy}."
            : result.ErrorMessage;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("api/loans")]
    public async Task<IActionResult> GetAllApi(CancellationToken ct = default) =>
        Ok((await loans.GetAllAsync(ct)).Select(ToApiModel));

    [HttpPost("api/loans")]
    public async Task<IActionResult> CreateApi([FromBody] CreateBookLoanViewModel? model, CancellationToken ct = default)
    {
        if (model?.LoanDate == null || !TryValidateModel(model))
            return BadRequest(new { message = "Dữ liệu phiếu mượn không hợp lệ." });
        var result = await loans.CreateAsync(model.BookId, model.ReaderAccountId, model.LoanDate.Value, ct);
        if (!result.IsSuccess) return BadRequest(new { message = result.ErrorMessage });
        return Created($"/api/loans/{result.Loan!.Id}", ToApiModel(result.Loan));
    }

    [HttpGet("api/loans/adjust-due-date/{proposedDate}")]
    public async Task<IActionResult> PreviewDueDateApi(DateOnly proposedDate, CancellationToken ct = default)
    {
        try
        {
            var adjustedDate = await loans.AdjustDueDateAsync(proposedDate, ct);
            return Ok(new { proposedDate, adjustedDate, wasAdjusted = proposedDate != adjustedDate });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private async Task<LoanIndexViewModel> BuildModel(CancellationToken ct) => new()
    {
        Loans = await loans.GetAllAsync(ct),
        Books = await db.Books.AsNoTracking().OrderBy(book => book.Title).ToListAsync(ct),
        Readers = await db.ReaderAccounts.AsNoTracking()
            .Where(reader => reader.Status == "Đang hoạt động").OrderBy(reader => reader.FullName).ToListAsync(ct),
        NewLoan = new CreateBookLoanViewModel { LoanDate = DateOnly.FromDateTime(DateTime.Today) }
    };

    private static object ToApiModel(BookLoan loan) => new
    {
        loan.Id, loan.BookId, bookTitle = loan.Book?.Title, loan.ReaderAccountId,
        readerName = loan.ReaderAccount?.FullName, loan.LoanDate, loan.OriginalDueDate, loan.DueDate,
        wasDueDateAdjusted = loan.OriginalDueDate != loan.DueDate
    };
}
