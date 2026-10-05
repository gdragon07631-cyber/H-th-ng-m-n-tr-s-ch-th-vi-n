using Microsoft.AspNetCore.Mvc;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

/// <summary>Quản trị hệ thống tạo tài khoản nhân sự, gán vai trò và khoá/mở khoá.</summary>
public sealed class StaffAccountController(
    IStaffAccountService staffAccountService,
    IAuditLogService auditLogService,
    IWebHostEnvironment environment) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var (admin, denied) = await RequireSystemAdminAsync(cancellationToken);
        if (denied is not null) return denied;

        ViewBag.CurrentAdminId = admin!.Id;
        return View(await staffAccountService.ListAsync(cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken = default)
    {
        var (_, denied) = await RequireSystemAdminAsync(cancellationToken);
        return denied ?? View("Form", new StaffAccountFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StaffAccountFormViewModel model, CancellationToken cancellationToken = default)
    {
        var (admin, denied) = await RequireSystemAdminAsync(cancellationToken);
        if (denied is not null) return denied;
        model.Id = null;
        if (!ModelState.IsValid) return View("Form", model);

        var result = await staffAccountService.CreateAsync(model, SetupUrl(), cancellationToken);
        if (!result.IsSuccess) return FormWithError(model, result);

        var account = result.Account!;
        await WriteLogAsync(admin!.Email, AuditActions.CreateAccount, account, null, cancellationToken);
        TempData["SuccessMessage"] = $"Đã tạo tài khoản {account.Email} với vai trò {AccountRoles.DisplayName(account.Role)}.";
        ReportSetupEmail(result);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken = default)
    {
        var (_, denied) = await RequireSystemAdminAsync(cancellationToken);
        if (denied is not null) return denied;

        var account = await staffAccountService.GetAsync(id, cancellationToken);
        if (account is null) return NotFound("Không tìm thấy tài khoản.");
        return View("Form", new StaffAccountFormViewModel
        {
            Id = account.Id,
            FullName = account.FullName,
            Email = account.Email,
            PhoneNumber = account.PhoneNumber ?? string.Empty,
            Role = account.Role,
            IsActive = account.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, StaffAccountFormViewModel model, CancellationToken cancellationToken = default)
    {
        var (admin, denied) = await RequireSystemAdminAsync(cancellationToken);
        if (denied is not null) return denied;
        model.Id = id;
        if (!ModelState.IsValid) return View("Form", model);

        var result = await staffAccountService.UpdateAsync(id, model, admin!.Id, cancellationToken);
        if (result.Status == StaffAccountStatus.NotFound) return NotFound(result.ErrorMessage);
        if (!result.IsSuccess) return FormWithError(model, result);

        await WriteLogAsync(admin.Email, AuditActions.UpdateAccount, result.Account!, "cập nhật thông tin", cancellationToken);
        TempData["SuccessMessage"] = $"Đã cập nhật tài khoản {result.Account!.Email}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        var (admin, denied) = await RequireSystemAdminAsync(cancellationToken);
        if (denied is not null) return denied;

        var result = await staffAccountService.SetActiveAsync(id, isActive, admin!.Id, cancellationToken);
        if (result.Status == StaffAccountStatus.NotFound) return NotFound(result.ErrorMessage);
        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.ErrorMessage;
            return RedirectToAction(nameof(Index));
        }

        await WriteLogAsync(admin.Email, AuditActions.UpdateAccount, result.Account!,
            isActive ? "mở khoá tài khoản" : "khoá tài khoản", cancellationToken);
        TempData["SuccessMessage"] = isActive
            ? $"Đã mở khoá tài khoản {result.Account!.Email}."
            : $"Đã khoá tài khoản {result.Account!.Email}. Mọi phiên đăng nhập của tài khoản này đã bị thu hồi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendSetup(int id, CancellationToken cancellationToken = default)
    {
        var (_, denied) = await RequireSystemAdminAsync(cancellationToken);
        if (denied is not null) return denied;

        var result = await staffAccountService.ResendSetupEmailAsync(id, SetupUrl(), cancellationToken);
        if (result.Status == StaffAccountStatus.NotFound) return NotFound(result.ErrorMessage);
        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.ErrorMessage;
            return RedirectToAction(nameof(Index));
        }

        TempData["SuccessMessage"] = $"Đã tạo liên kết đặt mật khẩu mới cho {result.Account!.Email}.";
        ReportSetupEmail(result);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> SetPassword(string? token, CancellationToken cancellationToken = default)
    {
        if (!await staffAccountService.IsSetupTokenValidAsync(token ?? string.Empty, cancellationToken))
        {
            ViewBag.InvalidToken = true;
            return View(new StaffSetPasswordViewModel());
        }
        return View(new StaffSetPasswordViewModel { Token = token! });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPassword(StaffSetPasswordViewModel model, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return View(model);

        var account = await staffAccountService.CompleteSetupAsync(model.Token, model.Password, cancellationToken);
        if (account is null)
        {
            ViewBag.InvalidToken = true;
            return View(new StaffSetPasswordViewModel());
        }

        await WriteLogAsync(account.Email, AuditActions.UpdateAccount, account, "đặt mật khẩu lần đầu", cancellationToken);
        ViewBag.Success = true;
        ViewBag.LoginController = account.Role switch
        {
            AccountRoles.Librarian => "Librarian",
            AccountRoles.LibraryManager => "Manager",
            _ => "Account"
        };
        return View(new StaffSetPasswordViewModel());
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

    private ViewResult FormWithError(StaffAccountFormViewModel model, StaffAccountResult result)
    {
        var field = result.Status switch
        {
            StaffAccountStatus.DuplicateEmail => nameof(model.Email),
            StaffAccountStatus.InvalidRole => nameof(model.Role),
            _ => string.Empty
        };
        ModelState.AddModelError(field, result.ErrorMessage ?? "Không thể lưu tài khoản.");
        return View("Form", model);
    }

    private void ReportSetupEmail(StaffAccountResult result)
    {
        if (result.EmailSent)
        {
            TempData["InfoMessage"] = $"Email đặt mật khẩu (hiệu lực 24 giờ) đã được gửi tới {result.Account!.Email}.";
            return;
        }

        TempData["WarningMessage"] = "Chưa gửi được email đặt mật khẩu. Kiểm tra cấu hình SMTP rồi dùng nút \"Gửi lại email\".";
        if (environment.IsDevelopment()) TempData["DevSetupLink"] = result.SetupLink;
    }

    private string SetupUrl() => Url.Action(nameof(SetPassword), "StaffAccount", null, Request.Scheme)!;

    private Task WriteLogAsync(string actor, string action, AdminAccount account, string? detail, CancellationToken cancellationToken) =>
        auditLogService.WriteAsync(
            actor,
            action,
            $"Tài khoản {AuditLogService.DescribeRole(account.Role)} #{account.Id} ({account.Email})" + (detail is null ? string.Empty : $" – {detail}"),
            AuditLogService.ClientIp(HttpContext),
            cancellationToken);
}
