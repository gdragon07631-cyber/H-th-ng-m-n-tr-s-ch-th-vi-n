using Microsoft.AspNetCore.Mvc;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

public sealed class ReaderRegistrationController(
    IReaderRegistrationService registrationService,
    ReaderRegistrationIpRateLimiter ipRateLimiter) : Controller
{
    [HttpGet]
    public IActionResult Register()
    {
        return View(new ReaderRegistrationViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(
        ReaderRegistrationViewModel model,
        CancellationToken cancellationToken = default)
    {
        // Keep date binding errors next to the field, in the form's language.
        if (ModelState.TryGetValue(nameof(model.DateOfBirth), out var dateState) &&
            dateState.Errors.Count > 0 && !string.IsNullOrWhiteSpace(dateState.AttemptedValue))
        {
            dateState.Errors.Clear();
            ModelState.AddModelError(nameof(model.DateOfBirth), "Ngày sinh không hợp lệ.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!ipRateLimiter.TryAcquire(ipAddress, out var registrationLease))
        {
            ModelState.AddModelError(string.Empty,
                "Địa chỉ IP này đã đạt giới hạn 3 lượt đăng ký trong một giờ. Vui lòng thử lại sau.");
            return View(model);
        }

        using (registrationLease)
        {
        var outcome = await registrationService.RegisterAsync(model, cancellationToken);

        if (!outcome.IsSuccess)
        {
            // 3 & 5. Hiển thị lỗi ngay trên biểu mẫu khi email đã được đăng ký kèm gợi ý "Quên mật khẩu"
            if (outcome.IsEmailDuplicate)
            {
                ModelState.AddModelError(
                    nameof(model.Email),
                    "Email này đã được đăng ký. Nếu đây là tài khoản của bạn, hãy sử dụng chức năng Quên mật khẩu.");
                ViewBag.ShowForgotPasswordSuggestion = true;
            }

            // 4. Hiển thị lỗi ngay trên biểu mẫu khi mã sinh viên hoặc mã cán bộ đã được đăng ký
            if (outcome.IsCodeDuplicate)
            {
                ModelState.AddModelError(
                    nameof(model.StudentOrStaffCode),
                    "Mã sinh viên/mã cán bộ này đã được đăng ký. Nếu đây là tài khoản của bạn, hãy sử dụng chức năng Quên mật khẩu.");
                ViewBag.ShowCodeForgotPasswordSuggestion = true;
            }

            // 8. Tài khoản bị từ chối do trùng email hoặc trùng mã KHÔNG được tạo thêm bản ghi mới
            return View(model);
        }

        registrationLease.Commit();

        // 7 & 8. Đăng ký thành công, thông báo hiển thị rõ trạng thái "Chờ duyệt"
        TempData["SuccessMessage"] = "Đăng ký tài khoản thành công!";
        TempData["AccountStatus"] = "Chờ duyệt";
        return RedirectToAction(nameof(RegisterSuccess));
        }
    }

    [HttpGet]
    public IActionResult ForgotPassword()
    {
        return View();
    }

    [HttpGet]
    public IActionResult RegisterSuccess()
    {
        if (TempData["SuccessMessage"] == null)
        {
            return RedirectToAction(nameof(Register));
        }

        ViewBag.SuccessMessage = TempData["SuccessMessage"];
        ViewBag.AccountStatus = TempData["AccountStatus"];
        ViewBag.Instruction = "Tài khoản của bạn đang chờ được thư viện phê duyệt. Vui lòng đến quầy thư viện và xuất trình thẻ sinh viên, thẻ cán bộ hoặc giấy tờ phù hợp để hoàn tất quá trình duyệt tài khoản.";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Profile(int? id, CancellationToken cancellationToken = default)
    {
        int targetId = GetCurrentLoggedInReaderId();
        if (targetId <= 0)
        {
            return RedirectToAction(nameof(Login));
        }
        if (id.HasValue && id.Value != targetId)
        {
            return Forbid();
        }

        var reader = await registrationService.GetReaderByIdAsync(targetId, cancellationToken);
        if (reader == null)
        {
            return NotFound("Không tìm thấy thông tin tài khoản Bạn đọc.");
        }

        return View(ToProfileViewModel(reader));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(
        ReaderProfileViewModel model, CancellationToken cancellationToken = default)
    {
        int readerId = GetCurrentLoggedInReaderId();
        if (readerId <= 0) return RedirectToAction(nameof(Login));

        var current = await registrationService.GetReaderByIdAsync(readerId, cancellationToken);
        if (current == null) return NotFound("Không tìm thấy thông tin tài khoản Bạn đọc.");

        if (!ModelState.IsValid)
        {
            CopyFixedProfileFields(model, current);
            model.CurrentPassword = string.Empty;
            ModelState.Remove(nameof(model.CurrentPassword));
            return View(model);
        }

        var emailChanged = !string.Equals(current.Email.Trim(), model.Email.Trim(), StringComparison.OrdinalIgnoreCase);
        if (emailChanged && string.IsNullOrWhiteSpace(model.CurrentPassword))
        {
            ModelState.AddModelError(nameof(model.CurrentPassword), "Vui lòng nhập mật khẩu hiện tại.");
            CopyFixedProfileFields(model, current);
            model.CurrentPassword = string.Empty;
            ModelState.SetModelValue(nameof(model.CurrentPassword), string.Empty, string.Empty);
            return View(model);
        }

        var updateResult = await registrationService.UpdateReaderContactAsync(
            readerId, model.PhoneNumber, model.Address, model.Email, model.CurrentPassword, cancellationToken);
        if (updateResult == ReaderContactUpdateResult.NotFound)
            return NotFound("Không tìm thấy thông tin tài khoản Bạn đọc.");
        if (updateResult == ReaderContactUpdateResult.InvalidCurrentPassword)
        {
            ModelState.AddModelError(nameof(model.CurrentPassword), "Mật khẩu hiện tại không chính xác.");
            CopyFixedProfileFields(model, current);
            model.CurrentPassword = string.Empty;
            ModelState.SetModelValue(nameof(model.CurrentPassword), string.Empty, string.Empty);
            return View(model);
        }

        TempData["ProfileSuccessMessage"] = emailChanged
            ? "Đổi email thành công."
            : "Lưu thông tin thành công.";
        return RedirectToAction(nameof(Profile));
    }

    private static ReaderProfileViewModel ToProfileViewModel(ReaderAccount reader) => new()
    {
        FullName = reader.FullName,
        DateOfBirth = reader.DateOfBirth,
        StudentOrStaffCode = reader.StudentOrStaffCode,
        Status = reader.Status,
        RejectionReason = reader.RejectionReason,
        PhoneNumber = reader.PhoneNumber,
        Address = reader.Address ?? string.Empty,
        Email = reader.Email,
        CardCode = reader.LibraryCard?.CardCode,
        CardType = reader.LibraryCard?.LibraryCardType?.Name,
        CardExpiresOn = reader.LibraryCard?.ExpiresOn,
        CardStatus = reader.LibraryCard?.Status,
        LibraryCard = reader.LibraryCard
    };

    private static void CopyFixedProfileFields(ReaderProfileViewModel model, ReaderAccount reader)
    {
        model.FullName = reader.FullName;
        model.DateOfBirth = reader.DateOfBirth;
        model.StudentOrStaffCode = reader.StudentOrStaffCode;
        model.Status = reader.Status;
        model.RejectionReason = reader.RejectionReason;
        model.CardCode = reader.LibraryCard?.CardCode;
        model.CardType = reader.LibraryCard?.LibraryCardType?.Name;
        model.CardExpiresOn = reader.LibraryCard?.ExpiresOn;
        model.CardStatus = reader.LibraryCard?.Status;
        model.LibraryCard = reader.LibraryCard;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(
        ReaderChangePasswordViewModel model, CancellationToken cancellationToken = default)
    {
        int readerId = GetCurrentLoggedInReaderId();
        if (readerId <= 0) return RedirectToAction(nameof(Login));

        if (!ModelState.IsValid)
        {
            TempData["PasswordErrorMessage"] = ModelState.Values
                .SelectMany(entry => entry.Errors)
                .Select(error => error.ErrorMessage)
                .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
                ?? "Vui lòng kiểm tra lại thông tin mật khẩu.";
            return RedirectToAction(nameof(Profile));
        }

        var result = await registrationService.ChangeReaderPasswordAsync(
            readerId, model.CurrentPassword, model.NewPassword, cancellationToken);
        switch (result)
        {
            case ReaderPasswordChangeResult.NotFound:
                return NotFound("Không tìm thấy thông tin tài khoản Bạn đọc.");
            case ReaderPasswordChangeResult.IncorrectCurrentPassword:
                TempData["PasswordErrorMessage"] = "Mật khẩu cũ không chính xác.";
                break;
            case ReaderPasswordChangeResult.PasswordRecentlyUsed:
                TempData["PasswordErrorMessage"] = "Mật khẩu mới không được trùng với 3 mật khẩu gần nhất.";
                break;
            case ReaderPasswordChangeResult.Success:
                TempData["PasswordSuccessMessage"] = "Đổi mật khẩu thành công.";
                break;
        }

        return RedirectToAction(nameof(Profile));
    }

    [HttpGet("api/reader/profile")]
    public async Task<IActionResult> GetProfileApi(CancellationToken cancellationToken = default)
    {
        var readerId = GetCurrentLoggedInReaderId();
        if (readerId <= 0)
            return Unauthorized(new { message = "Vui lòng đăng nhập trước khi xem thông tin cá nhân." });

        var reader = await registrationService.GetReaderByIdAsync(readerId, cancellationToken);
        if (reader == null)
            return NotFound(new { message = "Không tìm thấy thông tin tài khoản Bạn đọc." });

        return Ok(new
        {
            id = reader.Id,
            fullName = reader.FullName,
            studentOrStaffCode = reader.StudentOrStaffCode,
            status = reader.Status,
            rejectionReason = reader.RejectionReason
        });
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string email, string password, string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        ViewData["ReturnUrl"] = returnUrl;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            ModelState.AddModelError(string.Empty, "Vui lòng nhập đầy đủ email và mật khẩu.");
            return View();
        }

        var reader = await registrationService.AuthenticateReaderAsync(email, password, cancellationToken);
        if (reader == null)
        {
            ModelState.AddModelError(string.Empty, "Email hoặc mật khẩu không chính xác.");
            return View();
        }

        Response.Cookies.Append("reader_id", reader.Id.ToString(), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        });

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction(nameof(Profile), new { id = reader.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("reader_id");
        return RedirectToAction(nameof(Login));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HoldDocument(
        int documentId,
        int? readerId = null,
        CancellationToken cancellationToken = default)
    {
        int targetId = GetCurrentLoggedInReaderId();
        if (targetId <= 0)
        {
            return RedirectToAction(nameof(Login));
        }

        var outcome = await registrationService.HoldDocumentAsync(targetId, documentId, cancellationToken);
        if (!outcome.IsAllowed)
        {
            TempData["HoldErrorMessage"] = outcome.Message;
        }
        else
        {
            TempData["HoldSuccessMessage"] = outcome.Message;
        }

        return RedirectToAction(nameof(Profile), new { id = targetId });
    }

    [HttpPost("api/documents/{documentId}/hold")]
    public async Task<IActionResult> HoldDocumentApi(
        int documentId,
        [FromQuery] int? readerId = null,
        CancellationToken cancellationToken = default)
    {
        int targetId = GetCurrentLoggedInReaderId();
        if (targetId <= 0)
        {
            return Unauthorized(new { message = "Vui lòng đăng nhập trước khi thực hiện đặt giữ tài liệu." });
        }

        var outcome = await registrationService.HoldDocumentAsync(targetId, documentId, cancellationToken);
        if (!outcome.IsAllowed)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                isAllowed = false,
                status = "Rejected",
                message = outcome.Message
            });
        }

        return Ok(new
        {
            isAllowed = true,
            status = "Success",
            message = outcome.Message
        });
    }

    private int GetCurrentLoggedInReaderId()
    {
        if (Request.Cookies.TryGetValue("reader_id", out var idStr) && int.TryParse(idStr, out var id))
        {
            return id;
        }
        return 0;
    }
}
