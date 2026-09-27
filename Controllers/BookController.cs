using Microsoft.AspNetCore.Mvc;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

public sealed class BookController(
    IBookService bookService,
    IAuthorService authorService,
    ICategoryService categoryService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var books = await bookService.GetAllBooksAsync(cancellationToken);
        return View(books);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken = default)
    {
        var activeAuthors = await authorService.GetActiveAuthorsAsync(cancellationToken);
        var activeCategories = await categoryService.GetActiveAsync(cancellationToken);
        var viewModel = new CatalogBookViewModel
        {
            ActiveAuthors = activeAuthors,
            ActiveCategories = activeCategories
        };
        return View(viewModel);
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

        return View(bookDetails);
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
}
