using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;
using Project.Services;
using Xunit;

namespace Project.Tests;

public class ShelfDeletionGuardTests
{
    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Test1_XoaKeKhongCoBanSao_ThanhCong()
    {
        using var db = CreateDb();
        var warehouseService = new WarehouseService(db);
        var shelfService = new ShelfService(db);
        var warehouse = (await warehouseService.CreateAsync("KHO-EMPTY", "Kho trống", null, null)).Warehouse!;
        var shelf = (await shelfService.CreateAsync(warehouse.Id, "KE-EMPTY", "Kệ trống", null)).Shelf!;

        var result = await shelfService.DeleteAsync(shelf.Id);

        Assert.True(result.IsSuccess);
        Assert.False(await db.Shelves.AnyAsync(item => item.Id == shelf.Id));
    }

    [Fact]
    public async Task Test2_XoaKeCoBanSao_ThatBaiVaGiuDuLieu()
    {
        using var db = CreateDb();
        var warehouseService = new WarehouseService(db);
        var shelfService = new ShelfService(db);
        var warehouse = (await warehouseService.CreateAsync("KHO-COPY", "Kho có bản sao", null, null)).Warehouse!;
        var shelf = (await shelfService.CreateAsync(warehouse.Id, "KE-COPY", "Kệ có bản sao", null)).Shelf!;
        var book = new Book { Title = "Sách kiểm thử", AuthorId = 1 };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        var copy = new BookCopy { BookId = book.Id, ShelfId = shelf.Id, CopyCode = "COPY-001", Status = "Đang mượn" };
        db.BookCopies.Add(copy);
        await db.SaveChangesAsync();

        var result = await shelfService.DeleteAsync(shelf.Id);

        Assert.False(result.IsSuccess);
        Assert.True(result.HasLinkedBookCopies);
        Assert.Contains("Không thể xoá kệ vì đang có bản sao sách", result.ErrorMessage);
        Assert.True(await db.Shelves.AnyAsync(item => item.Id == shelf.Id));
        Assert.True(await db.BookCopies.AnyAsync(item => item.Id == copy.Id));
    }

    [Fact]
    public async Task Test3_XoaKeCoNhieuBanSao_VanBiChan()
    {
        using var db = CreateDb();
        var warehouseService = new WarehouseService(db);
        var shelfService = new ShelfService(db);
        var warehouse = (await warehouseService.CreateAsync("KHO-MULTI", "Kho nhiều bản sao", null, null)).Warehouse!;
        var shelf = (await shelfService.CreateAsync(warehouse.Id, "KE-MULTI", "Kệ nhiều bản sao", null)).Shelf!;
        db.Books.Add(new Book { Title = "Sách A", AuthorId = 1 });
        db.Books.Add(new Book { Title = "Sách B", AuthorId = 1 });
        await db.SaveChangesAsync();
        var books = await db.Books.ToListAsync();
        db.BookCopies.AddRange(
            new BookCopy { BookId = books[0].Id, ShelfId = shelf.Id, CopyCode = "COPY-M1" },
            new BookCopy { BookId = books[1].Id, ShelfId = shelf.Id, CopyCode = "COPY-M2" });
        await db.SaveChangesAsync();

        var result = await shelfService.DeleteAsync(shelf.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("Hãy chuyển hoặc gỡ hết bản sao", result.ErrorMessage);
        Assert.Equal(2, await db.BookCopies.CountAsync(copy => copy.ShelfId == shelf.Id));
    }
}
