using Microsoft.AspNetCore.Mvc;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

/// <summary>Quản trị hệ thống tra nhật ký hoạt động người dùng.</summary>
public sealed class AuditLogController(IAuditLogService auditLogService) : Controller
{
    public const int DisplayLimit = 500;

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index([FromQuery] AuditLogFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var staff = await auditLogService.GetSignedInStaffAsync(Request, cancellationToken);
        if (staff is null)
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Index)) });
        if (!AuditLogService.CanViewLogs(staff))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return View("AccessDenied");
        }

        filter ??= new AuditLogFilter();
        var model = new AuditLogIndexViewModel
        {
            Filter = filter,
            Actors = await auditLogService.GetActorsAsync(cancellationToken)
        };

        if (filter.FromDate > filter.ToDate)
            model.ErrorMessage = "Từ ngày không được lớn hơn đến ngày.";
        else if (!ModelState.IsValid)
            model.ErrorMessage = "Điều kiện lọc không hợp lệ. Vui lòng kiểm tra lại ngày đã nhập.";
        else
            model.Logs = await auditLogService.SearchAsync(filter, DisplayLimit, cancellationToken);

        return View(model);
    }
}
