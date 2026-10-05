using Microsoft.AspNetCore.Mvc;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

/// <summary>Tra cứu sách công khai cho bạn đọc: chỉ hiển thị đầu sách có ít nhất 1 bản sao.</summary>
public sealed class CatalogController(IBookService bookService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string? q, int page = 1, List<string>? categories = null, int? fromYear = null, int? toYear = null,
        bool availableOnly = false, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (fromYear.HasValue && toYear.HasValue && fromYear > toYear) return BadRequest("fromYear must not be greater than toYear.");
        var result = await bookService.SearchPublicCatalogPageAsync(q, page, 20, categories, fromYear, toYear, availableOnly, cancellationToken);
        var model = new PublicCatalogSearchViewModel
        {
            Keyword = q?.Trim(),
            Results = result.Items,
            Categories = await bookService.GetPublicCatalogCategoriesAsync(cancellationToken),
            SelectedCategories = categories ?? [],
            FromYear = fromYear,
            ToYear = toYear,
            AvailableOnly = availableOnly,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalItems = result.TotalItems
        };
        return View(model);
    }

    [HttpGet("api/books/search")]
    public async Task<IActionResult> SearchBooksApi(
        [FromQuery] string? keyword, [FromQuery] List<string>? categories = null,
        [FromQuery] int? fromYear = null, [FromQuery] int? toYear = null, [FromQuery] bool availableOnly = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (fromYear.HasValue && toYear.HasValue && fromYear > toYear)
            return BadRequest(new { error = "fromYear must not be greater than toYear." });
        var result = await bookService.SearchPublicCatalogPageAsync(
            keyword, page, pageSize, categories, fromYear, toYear, availableOnly, cancellationToken);
        return Ok(new { items = result.Items, result.Page, result.PageSize, totalItems = result.TotalItems, result.TotalPages });
    }

    [HttpGet("api/catalog/categories")]
    public async Task<IActionResult> SearchCategoriesApi(CancellationToken cancellationToken = default) =>
        Ok(await bookService.GetPublicCatalogCategoriesAsync(cancellationToken));

    [HttpGet("api/catalog")]
    public async Task<IActionResult> SearchApi([FromQuery] string? q, CancellationToken cancellationToken = default)
    {
        var results = await bookService.SearchPublicCatalogAsync(q, cancellationToken);
        return Ok(results.Select(book => new
        {
            id = book.Id,
            title = book.Title,
            subtitle = book.Subtitle,
            authors = book.Authors,
            category = book.CategoryName,
            isbn = book.Isbn,
            publicationYear = book.PublicationYear,
            availableCopies = book.AvailableCopies,
            totalCopies = book.TotalCopies
        }));
    }
}
