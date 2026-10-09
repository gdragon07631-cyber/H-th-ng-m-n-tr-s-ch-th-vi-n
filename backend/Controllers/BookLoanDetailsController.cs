using Microsoft.AspNetCore.Mvc;
using Project.Filters;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

[StaffOnly(AccountRoles.Librarian, AccountRoles.SystemAdmin, AccountRoles.LibraryManager)]
public sealed class BookLoanDetailsController(
    IBookLoanDetailsService detailsService,
    ILogger<BookLoanDetailsController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken = default)
    {
        try
        {
            var details = await detailsService.GetHoldConversionDetailsAsync(id, cancellationToken);
            if (details != null) return View(details);

            Response.StatusCode = StatusCodes.Status404NotFound;
            return View("Unavailable", "Không tìm thấy phiếu mượn được tạo từ đơn đặt giữ này.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not load hold-conversion loan details for loan {LoanId}.", id);
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            return View("Unavailable", "Không thể tải thông tin phiếu mượn lúc này. Vui lòng thử lại sau.");
        }
    }
}
