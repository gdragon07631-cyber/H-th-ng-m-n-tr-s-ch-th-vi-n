using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Project.Controllers;
using Project.Data;
using Project.Models;
using Project.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Project.Tests;

public sealed class PublicCatalogPerformanceTests
{
    private readonly ITestOutputHelper output;

    public PublicCatalogPerformanceTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    [Trait("Category", "Performance")]
    public async Task SearchApiBenchmarksSixQueriesAgainstFiveThousandBooks()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var technology = new Category { Name = "Công nghệ", Status = CategoryStatus.Active };
        var finance = new Category { Name = "Tài chính", Status = CategoryStatus.Active };
        var author = new Author { Name = "Nguyễn Văn An", Status = AuthorStatus.Active };
        var warehouse = new Warehouse { Code = "PERF", Name = "Performance fixture", Status = WarehouseStatus.Active };
        db.AddRange(technology, finance, author, warehouse);
        await db.SaveChangesAsync();
        var shelf = new Shelf { Code = "PERF-1", Name = "Performance fixture", WarehouseId = warehouse.Id };
        db.Shelves.Add(shelf);
        await db.SaveChangesAsync();

        var books = Enumerable.Range(1, 5000).Select(index => new Book
        {
            Title = index % 5 switch
            {
                0 => $"Lập trình Java database {index:D5}",
                1 => $"Kinh tế tài chính {index:D5}",
                2 => $"Quản trị dữ liệu {index:D5}",
                3 => $"Java cho người mới {index:D5}",
                _ => $"Cẩm nang thư viện {index:D5}"
            },
            Isbn = $"978000{index:D7}",
            AuthorId = author.Id,
            CategoryId = index % 5 == 1 ? finance.Id : technology.Id,
            PublicationYear = 2000 + index % 26
        }).ToList();
        db.Books.AddRange(books);
        await db.SaveChangesAsync();
        db.BookCopies.AddRange(books.Select((book, index) => new BookCopy
        {
            BookId = book.Id,
            ShelfId = shelf.Id,
            CopyCode = $"PERF-{index + 1:D5}",
            Status = index % 3 == 0 ? BookCopyStatus.OnLoan : BookCopyStatus.Available
        }));
        await db.SaveChangesAsync();

        var service = new BookService(db, NullLogger<BookService>.Instance);
        var controller = new CatalogController(service);
        var cases = new (string Name, string? Keyword, List<string>? Categories, int? From, int? To, bool Available)[]
        {
            ("Keyword search", "lap trinh", null, null, null, false),
            ("Keyword + category", "kinh tế", ["Tài chính"], null, null, false),
            ("Keyword + year range", "java", null, 2015, 2020, false),
            ("Keyword + available", "java", null, null, null, true),
            ("All filters", "kinh tế", ["Tài chính"], 2010, 2023, true),
            ("No result", "abcxyz123", null, null, null, false)
        };

        foreach (var testCase in cases)
        {
            _ = await controller.SearchBooksApi(testCase.Keyword, testCase.Categories, testCase.From, testCase.To, testCase.Available);
            var samples = new double[10];
            for (var index = 0; index < samples.Length; index++)
            {
                var timer = Stopwatch.StartNew();
                var response = await controller.SearchBooksApi(
                    testCase.Keyword, testCase.Categories, testCase.From, testCase.To, testCase.Available);
                timer.Stop();
                Assert.IsType<OkObjectResult>(response);
                samples[index] = timer.Elapsed.TotalMilliseconds;
            }

            var average = samples.Average();
            output.WriteLine($"{testCase.Name}: min={samples.Min():F1} ms, average={average:F1} ms, max={samples.Max():F1} ms");
            Assert.True(samples.Max() < 1500, $"{testCase.Name} exceeded 1.5 seconds on the isolated 5,000-book SQLite fixture.");
        }
    }
}
