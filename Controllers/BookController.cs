using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

public sealed class BookController(
    IBookService bookService,
    IAuthorService authorService,
    ICategoryService categoryService,
    IReaderRegistrationService registrationService,
    ApplicationDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        if (!await IsLibrarianSignedInAsync(cancellationToken))
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Index)) });

        var books = await bookService.GetAllBooksAsync(cancellationToken);
        return View(books);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken = default)
    {
        if (!await IsLibrarianSignedInAsync(cancellationToken))
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Create)) });

        var activeAuthors = await authorService.GetActiveAuthorsAsync(cancellationToken);
        var activeCategories = await categoryService.GetActiveAsync(cancellationToken);
        var viewModel = new CatalogBookViewModel
        {
            ActiveAuthors = activeAuthors,
            ActiveCategories = activeCategories
        };
        return View(viewModel);
    }

    private async Task<bool> IsLibrarianSignedInAsync(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue("admin_refresh", out var token) || string.IsNullOrWhiteSpace(token)) return false;
        var hash = TokenService.HashRefreshToken(token);
        return await dbContext.RefreshTokens.AnyAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTime.UtcNow && item.AdminAccount.IsActive && (item.AdminAccount.Role == AccountRoles.SystemAdmin || item.AdminAccount.Role == AccountRoles.LibraryManager), ct);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CatalogBookViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            model.ActiveAuthors = await authorService.GetActiveAuthorsAsync(cancellationToken);
            model.ActiveCategories = await categoryService.GetActiveAsync(cancellationToken);
            return View(model);
        }

        var outcome = await bookService.CatalogBookAsync(model, cancellationToken);
        if (!outcome.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, outcome.ErrorMessage ?? "Không thể biên mục sách.");
            model.ActiveAuthors = await authorService.GetActiveAuthorsAsync(cancellationToken);
            model.ActiveCategories = await categoryService.GetActiveAsync(cancellationToken);
            return View(model);
        }

        TempData["SuccessMessage"] = $"Biên mục sách \"{outcome.Book!.Title}\" thành công.";
        return RedirectToAction(nameof(Details), new { id = outcome.Book.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken = default)
    {
        var bookDetails = await bookService.GetBookDetailsAsync(id, cancellationToken);
        if (bookDetails == null)
        {
            return NotFound("Không tìm thấy thông tin sách.");
        }

        var readerId = await GetCurrentLoggedInReaderIdAsync(cancellationToken);
        if (readerId > 0)
        {
            var reader = await registrationService.GetReaderByIdAsync(readerId, cancellationToken);
            bookDetails.CanHold = string.Equals(reader?.Status, "Đang hoạt động", StringComparison.OrdinalIgnoreCase);
            bookDetails.IsReaderSignedIn = reader != null;
        }
        return View(bookDetails);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Hold(int id, CancellationToken cancellationToken = default)
    {
        var readerId = await GetCurrentLoggedInReaderIdAsync(cancellationToken);
        if (readerId <= 0)
            return RedirectToAction("Login", "ReaderRegistration", new { returnUrl = Url.Action(nameof(Details), new { id }) });

        var outcome = await registrationService.HoldDocumentAsync(readerId, id, cancellationToken);
        TempData[outcome.IsAllowed ? "HoldSuccessMessage" : "HoldErrorMessage"] = outcome.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    // ==========================================
    // REST APIs
    // ==========================================

    [HttpPost("api/books")]
    public async Task<IActionResult> CreateBookApi(
        [FromBody] CatalogBookViewModel request,
        CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Title) || request.AuthorId <= 0 || request.CategoryId <= 0)
        {
            return BadRequest(new { message = "Vui lòng nhập đầy đủ tiêu đề sách và tác giả hợp lệ." });
        }

        var outcome = await bookService.CatalogBookAsync(request, cancellationToken);
        if (!outcome.IsSuccess)
        {
            return BadRequest(new { message = outcome.ErrorMessage });
        }

        return StatusCode(StatusCodes.Status201Created, new
        {
            id = outcome.Book!.Id,
            title = outcome.Book.Title,
            isbn = outcome.Book.Isbn,
            authorId = outcome.Book.AuthorId,
            categoryId = outcome.Book.CategoryId,
            createdAtUtc = outcome.Book.CreatedAtUtc
        });
    }

    [HttpGet("api/books/{id}")]
    public async Task<IActionResult> GetBookDetailsApi(int id, CancellationToken cancellationToken = default)
    {
        var details = await bookService.GetBookDetailsAsync(id, cancellationToken);
        if (details == null)
        {
            return NotFound(new { message = "Không tìm thấy thông tin sách." });
        }

        return Ok(details);
    }

    private Task<int> GetCurrentLoggedInReaderIdAsync(CancellationToken cancellationToken) =>
        ReaderSessionCookies.GetReaderIdAsync(HttpContext, dataProtectionProvider, registrationService.GetReaderByIdAsync, cancellationToken);
}
