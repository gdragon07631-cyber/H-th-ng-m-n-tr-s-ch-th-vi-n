using Microsoft.AspNetCore.Mvc;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

/// <summary>
/// Quản trị hệ thống tra cứu và sửa mọi tài khoản bạn đọc để hỗ trợ ngay khi bạn đọc gặp sự cố:
/// sửa thông tin, xác nhận email, gửi lại email, gửi liên kết đặt lại mật khẩu, khoá/mở khoá, đăng xuất mọi thiết bị.
/// </summary>
public sealed class ReaderAccountController(
    IReaderAccountAdminService readerAccountService,
    IReaderEmailVerificationService emailVerificationService,
    IReaderPasswordResetService passwordResetService,
    IAuditLogService auditLogService,
    IWebHostEnvironment environment) : Controller
{
    private const int SearchLimit = 200;

    [HttpGet]
    public async Task<IActionResult> Index(string? q, string? status, CancellationToken cancellationToken = default)
    {
        var (_, denied) = await RequireSystemAdminAsync(cancellationToken);
        if (denied is not null) return denied;

        ViewBag.Query = q;
        ViewBag.Status = status;
        ViewBag.Limit = SearchLimit;
        return View(await readerAccountService.SearchAsync(q, status, SearchLimit, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken = default)
    {
        var (_, denied) = await RequireSystemAdminAsync(cancellationToken);
        if (denied is not null) return denied;

        var details = await readerAccountService.GetDetailsAsync(id, cancellationToken);
        return details is null ? NotFound("Không tìm thấy tài khoản bạn đọc.") : View(details);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind(Prefix = "Form")] ReaderAccountAdminFormViewModel model,
        CancellationToken cancellationToken = default)
    {
        var (admin, denied) = await RequireSystemAdminAsync(cancellationToken);
        if (denied is not null) return denied;
        model.Id = id;

        if (ModelState.IsValid)
        {
            var result = await readerAccountService.UpdateAsync(id, model, cancellationToken);
            if (result.Status == ReaderAccountAdminStatus.NotFound) return NotFound(result.ErrorMessage);
            if (result.IsSuccess)
            {
                await WriteLogAsync(admin!.Email, AuditActions.UpdateAccount, result.Reader!,
                    "quản trị sửa " + string.Join("; ", result.Changes!), cancellationToken);
                TempData["SuccessMessage"] = "Đã lưu thông tin bạn đọc.";
                return RedirectToAction(nameof(Details), new { id });
            }
            if (result.Status == ReaderAccountAdminStatus.NoChange)
            {
                TempData["InfoMessage"] = result.ErrorMessage;
                return RedirectToAction(nameof(Details), new { id });
            }

            var field = result.Status == ReaderAccountAdminStatus.DuplicateCode
                ? nameof(model.StudentOrStaffCode)
                : nameof(model.Email);
            ModelState.AddModelError("Form." + field, result.ErrorMessage!);
        }

        // Hiển thị lại trang chi tiết với dữ liệu vừa nhập và thông báo lỗi.
        var details = await readerAccountService.GetDetailsAsync(id, cancellationToken);
        if (details is null) return NotFound("Không tìm thấy tài khoản bạn đọc.");
        return View(nameof(Details), new ReaderAccountAdminDetailsViewModel
        {
            Reader = details.Reader,
            Form = model,
            ActiveHolds = details.ActiveHolds,
            History = details.History
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetLocked(int id, bool locked, string? reason, CancellationToken cancellationToken = default)
    {
        var (admin, denied) = await RequireSystemAdminAsync(cancellationToken);
        if (denied is not null) return denied;

        var result = await readerAccountService.SetLockedAsync(id, locked, reason, cancellationToken);
        if (result.Status == ReaderAccountAdminStatus.NotFound) return NotFound(result.ErrorMessage);
        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.ErrorMessage;
            return RedirectToAction(nameof(Details), new { id });
        }

        await WriteLogAsync(admin!.Email, AuditActions.UpdateAccount, result.Reader!,
            locked ? $"khoá tài khoản, lý do: {result.Reader!.LockReason}" : "mở khoá tài khoản", cancellationToken);
        TempData["SuccessMessage"] = locked
            ? "Đã khoá tài khoản. Bạn đọc bị đăng xuất khỏi mọi thiết bị và không thể đăng nhập cho tới khi được mở khoá."
            : "Đã mở khoá tài khoản. Bạn đọc có thể đăng nhập lại.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeSessions(int id, CancellationToken cancellationToken = default)
    {
        var (admin, denied) = await RequireSystemAdminAsync(cancellationToken);
        if (denied is not null) return denied;

        var result = await readerAccountService.RevokeSessionsAsync(id, cancellationToken);
        if (result.Status == ReaderAccountAdminStatus.NotFound) return NotFound(result.ErrorMessage);

        await WriteLogAsync(admin!.Email, AuditActions.UpdateAccount, result.Reader!, "đăng xuất khỏi mọi thiết bị", cancellationToken);
        TempData["SuccessMessage"] = "Đã đăng xuất bạn đọc khỏi mọi thiết bị.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmEmail(int id, CancellationToken cancellationToken = default)
    {
        var (admin, denied) = await RequireSystemAdminAsync(cancellationToken);
        if (denied is not null) return denied;

        var details = await readerAccountService.GetDetailsAsync(id, cancellationToken);
        if (details is null) return NotFound("Không tìm thấy tài khoản bạn đọc.");

        var outcome = await emailVerificationService.ConfirmByAdminAsync(id, cancellationToken);
        if (outcome.Result == EmailConfirmationResult.AlreadyConfirmed)
        {
            TempData["InfoMessage"] = "Email của bạn đọc đã được xác nhận từ trước.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await WriteLogAsync(admin!.Email, AuditActions.UpdateAccount, details.Reader, "quản trị xác nhận email thủ công", cancellationToken);
        if (outcome.IssuedCard is { } card)
        {
            await auditLogService.WriteAsync(admin.Email, AuditActions.IssueCard,
                $"Thẻ {card.CardCode} – bạn đọc #{id} (tự động khi quản trị xác nhận email)",
                AuditLogService.ClientIp(HttpContext), cancellationToken);
        }
        TempData["SuccessMessage"] = outcome.IssuedCard is { } issued
            ? $"Đã xác nhận email. Tài khoản đã được kích hoạt và cấp thẻ {issued.CardCode} (hạn {issued.ExpiresOn:dd/MM/yyyy})."
            : "Đã xác nhận email. Bạn đọc có thể đăng nhập ngay.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendConfirmation(int id, CancellationToken cancellationToken = default)
    {
        var (admin, denied) = await RequireSystemAdminAsync(cancellationToken);
        if (denied is not null) return denied;

        var details = await readerAccountService.GetDetailsAsync(id, cancellationToken);
        if (details is null) return NotFound("Không tìm thấy tài khoản bạn đọc.");
        if (details.Reader.EmailConfirmed)
        {
            TempData["InfoMessage"] = "Email của bạn đọc đã được xác nhận, không cần gửi lại.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await emailVerificationService.SendAsync(details.Reader, ReaderUrl("ConfirmEmail"), cancellationToken);
        await WriteLogAsync(admin!.Email, AuditActions.UpdateAccount, details.Reader, "quản trị gửi lại email xác nhận", cancellationToken);
        ReportEmail(result.Sent, result.Link, $"Đã gửi lại email xác nhận tới {details.Reader.Email}.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendPasswordReset(int id, CancellationToken cancellationToken = default)
    {
        var (admin, denied) = await RequireSystemAdminAsync(cancellationToken);
        if (denied is not null) return denied;

        var details = await readerAccountService.GetDetailsAsync(id, cancellationToken);
        if (details is null) return NotFound("Không tìm thấy tài khoản bạn đọc.");

        // Quản trị không đặt hay xem mật khẩu của bạn đọc: chỉ gửi liên kết để bạn đọc tự đặt mật khẩu mới.
        var accepted = await passwordResetService.RequestAsync(details.Reader.Email, ReaderUrl("ResetPassword"), cancellationToken);
        if (!accepted)
        {
            TempData["ErrorMessage"] = "Email này đã yêu cầu đặt lại mật khẩu quá nhiều lần gần đây. Vui lòng thử lại sau.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await WriteLogAsync(admin!.Email, AuditActions.ResetPassword, details.Reader, "quản trị gửi liên kết đặt lại mật khẩu", cancellationToken);
        TempData["SuccessMessage"] =
            $"Đã gửi liên kết đặt lại mật khẩu (hiệu lực 30 phút) tới {details.Reader.Email}. Nếu bạn đọc không thấy, nhắc kiểm tra thư mục Spam.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<(AdminAccount? Admin, IActionResult? Denied)> RequireSystemAdminAsync(CancellationToken cancellationToken)
    {
        var staff = await auditLogService.GetSignedInStaffAsync(Request, cancellationToken);
        if (staff is null)
            return (null, RedirectToAction("Login", "Account", new { returnUrl = Request.Path.Value }));
        if (staff.Role != AccountRoles.SystemAdmin)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return (null, View("AccessDenied"));
        }
        return (staff, null);
    }

    private void ReportEmail(bool sent, string? link, string successMessage)
    {
        if (sent)
        {
            TempData["SuccessMessage"] = successMessage;
            return;
        }
        TempData["WarningMessage"] = "Chưa gửi được email. Kiểm tra cấu hình SMTP hoặc dùng nút \"Xác nhận email thủ công\".";
        if (environment.IsDevelopment()) TempData["DevLink"] = link;
    }

    private string ReaderUrl(string action) => Url.Action(action, "ReaderRegistration", null, Request.Scheme)!;

    private Task WriteLogAsync(string actor, string action, ReaderAccount reader, string detail, CancellationToken cancellationToken) =>
        auditLogService.WriteAsync(
            actor,
            action,
            $"Tài khoản bạn đọc #{reader.Id} ({reader.Email}) – {detail}",
            AuditLogService.ClientIp(HttpContext),
            cancellationToken);
}
