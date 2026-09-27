using Microsoft.AspNetCore.Mvc;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

public sealed class AuthorController(
    IAuthorService authorService) : Controller
{
    // ==========================================
    // MVC VIEW ACTIONS
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var authors = await authorService.GetAllAuthorsAsync(cancellationToken);
        var viewModel = new AuthorIndexViewModel
        {
            Authors = authors,
            NewAuthor = new CreateAuthorViewModel()
        };
        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CreateAuthorViewModel newAuthor,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            var authors = await authorService.GetAllAuthorsAsync(cancellationToken);
            return View(nameof(Index), new AuthorIndexViewModel
            {
                Authors = authors,
                NewAuthor = newAuthor
            });
        }

        var outcome = await authorService.CreateAuthorAsync(newAuthor.Name, newAuthor.Note, cancellationToken);
        if (!outcome.IsSuccess)
        {
            ModelState.AddModelError("NewAuthor.Name", outcome.ErrorMessage ?? "Tác giả với tên này đã tồn tại.");
            var authors = await authorService.GetAllAuthorsAsync(cancellationToken);
            return View(nameof(Index), new AuthorIndexViewModel
            {
                Authors = authors,
                NewAuthor = newAuthor
            });
        }

        TempData["SuccessMessage"] = $"Thêm tác giả \"{outcome.Author!.Name}\" thành công.";
        return RedirectToAction(nameof(Index));
    }

    // ==========================================
    // REST APIs
    // ==========================================

    /// <summary>
    /// API 1: Lấy danh sách Tác giả cho màn hình quản lý danh mục.
    /// Bao gồm: Tên tác giả, Ghi chú, Trạng thái.
    /// </summary>
    [HttpGet("api/authors")]
    public async Task<IActionResult> GetAllAuthorsApi(CancellationToken cancellationToken = default)
    {
        var authors = await authorService.GetAllAuthorsAsync(cancellationToken);
        var result = authors.Select(a => new
        {
            id = a.Id,
            name = a.Name,
            note = a.Note,
            status = a.Status,
            createdAtUtc = a.CreatedAtUtc
        });
        return Ok(result);
    }

    /// <summary>
    /// API 2: Lấy danh sách Tác giả đang hoạt động cho ô chọn khi biên mục sách.
    /// CHỈ trả về Author có Status = "Hoạt động".
    /// </summary>
    [HttpGet("api/authors/active")]
    [HttpGet("Author/ActiveAuthors")]
    public async Task<IActionResult> GetActiveAuthorsApi(CancellationToken cancellationToken = default)
    {
        var activeAuthors = await authorService.GetActiveAuthorsAsync(cancellationToken);
        var result = activeAuthors.Select(a => new
        {
            id = a.Id,
            name = a.Name,
            note = a.Note,
            status = a.Status
        });
        return Ok(result);
    }

    /// <summary>
    /// API Thêm mới Tác giả: Nhận Name và Note, kiểm tra trùng, trả về kết quả hoặc lỗi.
    /// </summary>
    [HttpPost("api/authors")]
    public async Task<IActionResult> CreateAuthorApi(
        [FromBody] CreateAuthorDto request,
        CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new
            {
                message = "Họ tên tác giả là bắt buộc."
            });
        }

        var outcome = await authorService.CreateAuthorAsync(request.Name, request.Note, cancellationToken);
        if (!outcome.IsSuccess)
        {
            return BadRequest(new
            {
                message = outcome.ErrorMessage ?? "Tác giả với tên này đã tồn tại trong hệ thống."
            });
        }

        return StatusCode(StatusCodes.Status201Created, new
        {
            id = outcome.Author!.Id,
            name = outcome.Author.Name,
            note = outcome.Author.Note,
            status = outcome.Author.Status,
            createdAtUtc = outcome.Author.CreatedAtUtc
        });
    }
}
