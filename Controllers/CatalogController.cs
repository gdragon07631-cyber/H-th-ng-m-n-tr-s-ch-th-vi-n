using Microsoft.AspNetCore.Mvc;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

/// <summary>Tra cứu sách công khai cho bạn đọc: chỉ hiển thị đầu sách có ít nhất 1 bản sao.</summary>
public sealed class CatalogController(IBookService bookService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? q, CancellationToken cancellationToken = default)
    {
        var model = new PublicCatalogSearchViewModel
        {
            Keyword = q?.Trim(),
            Results = await bookService.SearchPublicCatalogAsync(q, cancellationToken)
        };
        return View(model);
    }

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
