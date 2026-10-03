using Project.Filters;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

[StaffOnly(AccountRoles.Librarian, AccountRoles.SystemAdmin, AccountRoles.LibraryManager)]
public sealed class BookController(
    IBookService bookService,
    IAuthorService authorService,
    ICategoryService categoryService,
    IReaderRegistrationService registrationService,
    ApplicationDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    IBookCoverThumbnailService thumbnailService) : Controller
{
    private const long MaxCoverBytes = 3 * 1024 * 1024;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        if (!await IsLibrarianSignedInAsync(cancellationToken))
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Index)) });

        var books = await bookService.GetAllBooksAsync(cancellationToken);
        await EnsureExistingBookThumbnailsAsync(books, cancellationToken);
        ViewData["CopyCounts"] = await bookService.GetCopyCountsAsync(books.Select(book => book.Id), cancellationToken);
        return View(books);
    }

    private async Task EnsureExistingBookThumbnailsAsync(IReadOnlyList<Book> books, CancellationToken cancellationToken)
    {
        var coverDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "book-covers");
        var thumbnailDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "book-thumbnails");
        var changed = false;

        foreach (var book in books)
        {
            if (string.IsNullOrWhiteSpace(book.CoverImagePath) || !string.IsNullOrWhiteSpace(book.ThumbnailImagePath)) continue;
            var fileName = Path.GetFileName(book.CoverImagePath);
            if (!fileName.StartsWith($"{book.Id}-", StringComparison.Ordinal)) continue;
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (extension is not (".jpg" or ".jpeg" or ".png")) continue;

            var sourcePath = Path.Combine(coverDirectory, fileName);
            if (!System.IO.File.Exists(sourcePath)) continue;

            Directory.CreateDirectory(thumbnailDirectory);
            var thumbnailPath = Path.Combine(thumbnailDirectory, fileName);
            try
            {
                await using var source = System.IO.File.OpenRead(sourcePath);
                await thumbnailService.CreateAsync(source, thumbnailPath, cancellationToken);
                book.ThumbnailImagePath = $"/uploads/book-thumbnails/{fileName}";
                changed = true;
            }
            catch (SixLabors.ImageSharp.ImageFormatException)
            {
                // An unreadable legacy cover remains visible using the original image path.
            }
        }

        if (changed) await dbContext.SaveChangesAsync(cancellationToken);
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
        return await dbContext.RefreshTokens.AnyAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTime.UtcNow && item.AdminAccount.IsActive && (item.AdminAccount.Role == AccountRoles.Librarian || item.AdminAccount.Role == AccountRoles.SystemAdmin || item.AdminAccount.Role == AccountRoles.LibraryManager), ct);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CatalogBookViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            await FillBookFormListsAsync(model, cancellationToken);
            return View(model);
        }

        var outcome = await bookService.CatalogBookAsync(model, cancellationToken);
        if (!outcome.IsSuccess)
        {
            if (outcome.RequiresTitleConfirmation)
                model.DuplicateTitleMatches = outcome.DuplicateTitleMatches;
            else
                ModelState.AddModelError(outcome.IsDuplicateIsbn ? nameof(model.Isbn) : string.Empty, outcome.ErrorMessage ?? "Không thể biên mục sách.");
            await FillBookFormListsAsync(model, cancellationToken);
            return View(model);
        }

        TempData["SuccessMessage"] = $"Biên mục sách \"{outcome.Book!.Title}\" thành công.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken = default)
    {
        if (!await IsLibrarianSignedInAsync(cancellationToken))
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Edit), new { id }) });

        var model = await bookService.GetBookForEditAsync(id, cancellationToken);
        if (model == null) return NotFound("Không tìm thấy thông tin sách.");
        await FillBookFormListsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CatalogBookViewModel model, CancellationToken cancellationToken = default)
    {
        if (!await IsLibrarianSignedInAsync(cancellationToken))
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Edit), new { id }) });

        model.Id = id;
        if (ModelState.IsValid)
        {
            var outcome = await bookService.UpdateBookAsync(id, model, cancellationToken);
            if (outcome.IsSuccess)
            {
                TempData["SuccessMessage"] = $"Cập nhật đầu sách \"{outcome.Book!.Title}\" thành công.";
                return RedirectToAction(nameof(Index));
            }
            if (!await dbContext.Books.AnyAsync(book => book.Id == id, cancellationToken))
                return NotFound("Không tìm thấy thông tin sách.");
            ModelState.AddModelError(outcome.IsDuplicateIsbn ? nameof(model.Isbn) : string.Empty, outcome.ErrorMessage ?? "Không thể cập nhật đầu sách.");
        }

        await FillBookFormListsAsync(model, cancellationToken);
        return View(model);
    }

    /// <summary>Nạp danh sách tác giả/thể loại đang hoạt động và các tác giả đang được chọn để hiển thị lại trên form.</summary>
    private async Task FillBookFormListsAsync(CatalogBookViewModel model, CancellationToken cancellationToken)
    {
        model.ActiveAuthors = await authorService.GetActiveAuthorsAsync(cancellationToken);
        model.ActiveCategories = await categoryService.GetActiveAsync(cancellationToken);
        if (model.Id > 0 && model.CategoryId > 0 && model.ActiveCategories.All(category => category.Id != model.CategoryId))
        {
            var current = await dbContext.Categories.AsNoTracking().Include(category => category.Parent)
                .FirstOrDefaultAsync(category => category.Id == model.CategoryId, cancellationToken);
            if (current != null) model.ActiveCategories = [.. model.ActiveCategories, current];
        }

        var selectedIds = model.ResolveAuthorIds();
        var selected = await dbContext.Authors.AsNoTracking()
            .Where(author => selectedIds.Contains(author.Id))
            .ToListAsync(cancellationToken);
        model.SelectedAuthors = selectedIds
            .Select(id => selected.FirstOrDefault(author => author.Id == id))
            .OfType<Author>()
            .ToList();
    }

    [HttpGet]
    [PublicAction]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken = default)
    {
        var bookDetails = await bookService.GetBookDetailsAsync(id, cancellationToken);
        if (bookDetails == null)
        {
            return NotFound("Không tìm thấy thông tin sách.");
        }

        var readerId = await GetCurrentLoggedInReaderIdAsync(cancellationToken);
        bookDetails.IsLibrarian = await IsLibrarianSignedInAsync(cancellationToken);
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
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<IActionResult> UploadCover(int id, IFormFile? coverImage, CancellationToken cancellationToken = default)
    {
        if (!await IsLibrarianSignedInAsync(cancellationToken))
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Details), new { id }) });

        if (!await dbContext.Books.AnyAsync(book => book.Id == id, cancellationToken)) return NotFound();
        if (coverImage == null || coverImage.Length == 0)
            return CoverUploadError(id, "Vui lòng chọn ảnh bìa để tải lên.");
        if (coverImage.Length > MaxCoverBytes)
            return CoverUploadError(id, "Ảnh bìa vượt quá giới hạn 3MB.");

        var extension = Path.GetExtension(coverImage.FileName).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png"))
            return CoverUploadError(id, "Sai định dạng ảnh. Chỉ chấp nhận JPG/JPEG hoặc PNG.");

        var header = new byte[8];
        await using (var input = coverImage.OpenReadStream())
        {
            var read = await input.ReadAsync(header, cancellationToken);
            var isPng = read >= 8 && header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var isJpeg = read >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff;
            if (!(extension == ".png" && isPng) && !(extension is ".jpg" or ".jpeg" && isJpeg))
                return CoverUploadError(id, "Nội dung ảnh không khớp định dạng JPG/JPEG hoặc PNG.");
        }

        var coverFileName = $"{id}-{Guid.NewGuid():N}{extension}";
        var relativePath = $"/uploads/book-covers/{coverFileName}";
        var thumbnailRelativePath = $"/uploads/book-thumbnails/{coverFileName}";
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "book-covers");
        var thumbnailDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "book-thumbnails");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(thumbnailDirectory);
        var fullPath = Path.Combine(directory, Path.GetFileName(relativePath));
        var thumbnailFullPath = Path.Combine(thumbnailDirectory, coverFileName);
        try
        {
            await using (var output = System.IO.File.Create(fullPath))
                await coverImage.CopyToAsync(output, cancellationToken);

            await using var source = coverImage.OpenReadStream();
            await thumbnailService.CreateAsync(source, thumbnailFullPath, cancellationToken);
        }
        catch (SixLabors.ImageSharp.InvalidImageContentException)
        {
            TryDeleteFile(fullPath);
            TryDeleteFile(thumbnailFullPath);
            return CoverUploadError(id, "Nội dung ảnh không hợp lệ hoặc vượt quá giới hạn xử lý.");
        }
        catch (SixLabors.ImageSharp.ImageFormatException)
        {
            TryDeleteFile(fullPath);
            TryDeleteFile(thumbnailFullPath);
            return CoverUploadError(id, "Nội dung tệp không phải ảnh JPG/JPEG hoặc PNG hợp lệ.");
        }

        var book = await dbContext.Books.FirstAsync(book => book.Id == id, cancellationToken);
        var previousCoverPath = book.CoverImagePath;
        var previousThumbnailPath = book.ThumbnailImagePath;
        book.CoverImagePath = relativePath;
        book.ThumbnailImagePath = thumbnailRelativePath;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            book.CoverImagePath = previousCoverPath;
            book.ThumbnailImagePath = previousThumbnailPath;
            TryDeleteFile(fullPath);
            TryDeleteFile(thumbnailFullPath);
            throw;
        }

        DeleteSupersededBookImage(previousCoverPath, "book-covers", id, relativePath);
        DeleteSupersededBookImage(previousThumbnailPath, "book-thumbnails", id, thumbnailRelativePath);
        TempData["SuccessMessage"] = "Tải ảnh bìa thành công.";
        if (!string.IsNullOrWhiteSpace(previousCoverPath))
            TempData["SuccessMessage"] = "Thay ảnh bìa thành công.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private static void DeleteSupersededBookImage(string? previousPath, string folder, int bookId, string currentPath)
    {
        if (string.IsNullOrWhiteSpace(previousPath) || string.Equals(previousPath, currentPath, StringComparison.Ordinal)) return;
        var prefix = $"/uploads/{folder}/";
        if (!previousPath.StartsWith(prefix, StringComparison.Ordinal)) return;
        var fileName = previousPath[prefix.Length..];
        if (fileName.Contains('/') || fileName.Contains('\\') || !fileName.StartsWith($"{bookId}-", StringComparison.Ordinal)) return;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png")) return;

        var directory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", folder);
        TryDeleteFile(Path.Combine(directory, fileName));
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        catch (IOException)
        {
            // A committed replacement remains valid if stale-file cleanup cannot complete.
        }
        catch (UnauthorizedAccessException)
        {
            // A committed replacement remains valid if stale-file cleanup cannot complete.
        }
    }

    private IActionResult CoverUploadError(int id, string message)
    {
        TempData["CoverUploadError"] = message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PublicAction]
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
        if (request == null || string.IsNullOrWhiteSpace(request.Title) || request.ResolveAuthorIds().Count == 0 || request.CategoryId <= 0)
        {
            return BadRequest(new { message = "Vui lòng nhập đầy đủ tiêu đề sách và tác giả hợp lệ." });
        }

        var outcome = await bookService.CatalogBookAsync(request, cancellationToken);
        if (outcome.RequiresTitleConfirmation)
        {
            // Gửi lại kèm "confirmedDuplicateTitle" bằng đúng nhan đề này để xác nhận vẫn tạo đầu sách mới.
            return Conflict(new { message = outcome.ErrorMessage, requiresConfirmation = true, duplicateTitleMatches = outcome.DuplicateTitleMatches });
        }
        if (outcome.IsDuplicateIsbn)
        {
            return Conflict(new { message = outcome.ErrorMessage });
        }
        if (!outcome.IsSuccess)
        {
            return BadRequest(new { message = outcome.ErrorMessage });
        }

        return StatusCode(StatusCodes.Status201Created, new
        {
            id = outcome.Book!.Id,
            title = outcome.Book.Title,
            subtitle = outcome.Book.Subtitle,
            isbn = outcome.Book.Isbn,
            publisher = outcome.Book.Publisher,
            publicationYear = outcome.Book.PublicationYear,
            pageCount = outcome.Book.PageCount,
            authorId = outcome.Book.AuthorId,
            authorIds = outcome.Book.BookAuthors.OrderBy(link => link.SortOrder).Select(link => link.AuthorId).ToArray(),
            categoryId = outcome.Book.CategoryId,
            createdAtUtc = outcome.Book.CreatedAtUtc
        });
    }

    [HttpGet("api/books/{id}")]
    [PublicAction]
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
