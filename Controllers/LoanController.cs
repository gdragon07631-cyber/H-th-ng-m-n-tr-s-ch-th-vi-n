using Project.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

[StaffOnly(AccountRoles.SystemAdmin, AccountRoles.LibraryManager)]
public sealed class LoanController(IBookLoanService loans, ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct = default)
    {
        if (!await IsLibrarianSignedInAsync(ct)) return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Index)) });
        return View(await BuildModel(ct));
    }

    private async Task<bool> IsLibrarianSignedInAsync(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue("admin_refresh", out var token) || string.IsNullOrWhiteSpace(token)) return false;
        var hash = TokenService.HashRefreshToken(token);
        return await db.RefreshTokens.AnyAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTime.UtcNow && item.AdminAccount.IsActive && (item.AdminAccount.Role == AccountRoles.SystemAdmin || item.AdminAccount.Role == AccountRoles.LibraryManager), ct);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateBookLoanViewModel model, CancellationToken ct = default)
    {
        if (!ModelState.IsValid || model.LoanDate == null)
        {
            TempData["ErrorMessage"] = ModelState.Values.SelectMany(value => value.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu phiếu mượn không hợp lệ.";
            return View(nameof(Index), await BuildModel(ct));
        }

        var result = await loans.CreateAsync(model.BookId, model.ReaderAccountId, model.LoanDate.Value, ct);
        await LogLoanAsync(AuditActions.CreateLoan, model.BookId, model.ReaderAccountId, result.IsSuccess
            ? $"lập phiếu mượn #{result.Loan!.Id}, ngày mượn {model.LoanDate:dd/MM/yyyy}, hạn trả {result.Loan!.DueDate:dd/MM/yyyy}"
            : $"lập phiếu mượn thất bại: {result.ErrorMessage}", ct);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? $"Đã tạo phiếu mượn. Hạn trả: {result.Loan!.DueDate:dd/MM/yyyy}."
            : result.ErrorMessage;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Renew(long id, CancellationToken ct = default)
    {
        var result = await loans.RenewAsync(id, DateOnly.FromDateTime(DateTime.Today), ct);
        await LogRenewAsync(id, result, ct);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? $"Đã gia hạn phiếu mượn. Hạn trả mới: {result.Loan!.DueDate:dd/MM/yyyy}. Số lần gia hạn: {result.Loan.RenewalCount} / {result.Loan.ReaderAccount!.LibraryCard!.LibraryCardType!.MaxRenewals}."
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
        await LogLoanAsync(AuditActions.CreateLoan, model.BookId, model.ReaderAccountId, result.IsSuccess
            ? $"lập phiếu mượn #{result.Loan!.Id}, ngày mượn {model.LoanDate:dd/MM/yyyy}, hạn trả {result.Loan!.DueDate:dd/MM/yyyy}"
            : $"lập phiếu mượn thất bại: {result.ErrorMessage}", ct);
        if (!result.IsSuccess) return BadRequest(new { message = result.ErrorMessage });
        return Created($"/api/loans/{result.Loan!.Id}", ToApiModel(result.Loan));
    }

    [HttpPost("api/loans/{id:long}/renew")]
    public async Task<IActionResult> RenewApi(long id, CancellationToken ct = default)
    {
        var result = await loans.RenewAsync(id, DateOnly.FromDateTime(DateTime.Today), ct);
        await LogRenewAsync(id, result, ct);
        if (!result.IsSuccess) return BadRequest(new { success = false, message = result.ErrorMessage, reason = result.ReasonCode });
        return Ok(new
        {
            success = true,
            message = "Gia hạn phiếu mượn thành công.",
            data = new
            {
                id = result.Loan!.Id,
                oldDueDate = result.OldDueDate,
                newDueDate = result.Loan.DueDate,
                renewalCount = result.Loan.RenewalCount,
                renewalLimit = result.Loan.ReaderAccount!.LibraryCard!.LibraryCardType!.MaxRenewals
            }
        });
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

    private async Task LogLoanAsync(string action, int bookId, int readerId, string detail, CancellationToken ct)
    {
        var audit = HttpContext?.RequestServices?.GetService<IAuditLogService>();
        if (audit is null) return;
        var staff = await audit.GetSignedInStaffAsync(Request, ct);
        var title = await db.Books.AsNoTracking().Where(book => book.Id == bookId).Select(book => book.Title).SingleOrDefaultAsync(ct);
        var reader = await db.ReaderAccounts.AsNoTracking().Where(item => item.Id == readerId)
            .Select(item => new { item.FullName, item.Email }).SingleOrDefaultAsync(ct);
        await audit.WriteAsync(
            staff?.Email ?? "Không xác định",
            action,
            $"Sách \"{title}\" (#{bookId}) – bạn đọc #{readerId} {reader?.FullName} ({reader?.Email}) – {detail}",
            AuditLogService.ClientIp(HttpContext!),
            ct);
    }

    private async Task LogRenewAsync(long loanId, RenewBookLoanOutcome result, CancellationToken ct)
    {
        var loan = await db.BookLoans.AsNoTracking().Where(item => item.Id == loanId)
            .Select(item => new { item.BookId, item.ReaderAccountId }).SingleOrDefaultAsync(ct);
        if (loan is null) return;
        await LogLoanAsync(AuditActions.RenewLoan, loan.BookId, loan.ReaderAccountId, result.IsSuccess
            ? $"gia hạn phiếu mượn #{loanId}: hạn trả {result.OldDueDate:dd/MM/yyyy} → {result.Loan!.DueDate:dd/MM/yyyy}, lần gia hạn thứ {result.Loan.RenewalCount}"
            : $"gia hạn phiếu mượn #{loanId} thất bại: {result.ErrorMessage}", ct);
    }

    private static object ToApiModel(BookLoan loan) => new
    {
        loan.Id, loan.BookId, bookTitle = loan.Book?.Title, loan.ReaderAccountId,
        readerName = loan.ReaderAccount?.FullName, loan.LoanDate, loan.OriginalDueDate, loan.DueDate,
        renewalCount = loan.RenewalCount,
        renewalLimit = loan.ReaderAccount?.LibraryCard?.LibraryCardType?.MaxRenewals ?? 0,
        wasDueDateAdjusted = loan.OriginalDueDate != loan.DueDate,
        canRenew = loan.DueDate >= DateOnly.FromDateTime(DateTime.Today)
    };
}
