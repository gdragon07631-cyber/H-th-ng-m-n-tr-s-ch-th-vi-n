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
                    "Email này đã được đăng ký. Bạn có thể sử dụng chức năng \"Quên mật khẩu\" để khôi phục mật khẩu.");
                ViewBag.ShowForgotPasswordSuggestion = true;
            }

            // 4. Hiển thị lỗi ngay trên biểu mẫu khi mã sinh viên hoặc mã cán bộ đã được đăng ký
            if (outcome.IsCodeDuplicate)
            {
                ModelState.AddModelError(
                    nameof(model.StudentOrStaffCode),
                    "Mã sinh viên hoặc mã cán bộ này đã được đăng ký trong hệ thống.");
            }

            // 8. Tài khoản bị từ chối do trùng email hoặc trùng mã KHÔNG được tạo thêm bản ghi mới
            return View(model);
        }

        registrationLease.Commit();

        // 7 & 8. Đăng ký thành công, thông báo hiển thị rõ trạng thái "Chờ duyệt"
        TempData["SuccessMessage"] = "Đăng ký tài khoản thành công. Tài khoản của bạn đang ở trạng thái Chờ duyệt.";
        TempData["AccountStatus"] = "Chờ duyệt";
        return RedirectToAction(nameof(RegisterSuccess));
        }
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
        ViewBag.Instruction = "Vui lòng đến quầy thư viện và xuất trình giấy tờ tùy thân (thẻ sinh viên / thẻ cán bộ / CCCD) để được duyệt tài khoản.";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Profile(int? id, CancellationToken cancellationToken = default)
    {
        int targetId = id ?? GetCurrentLoggedInReaderId();
        if (targetId <= 0)
        {
            return RedirectToAction(nameof(Login));
        }

        var reader = await registrationService.GetReaderByIdAsync(targetId, cancellationToken);
        if (reader == null)
        {
            return NotFound("Không tìm thấy thông tin tài khoản Bạn đọc.");
        }

        return View(reader);
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
        int targetId = readerId ?? GetCurrentLoggedInReaderId();
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
        int targetId = readerId ?? GetCurrentLoggedInReaderId();
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
