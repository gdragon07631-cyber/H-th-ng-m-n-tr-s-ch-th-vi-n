using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project.Controllers;
using Project.Data;
using Project.Models;
using Project.Services;
using Xunit;

namespace Project.Tests;

public class WarehouseAndShelfTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Test1_ThemKho_ThanhCong()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var warehouseService = new WarehouseService(db);

        // Act
        var result = await warehouseService.CreateAsync("KHO-01", "Kho Sách Khoa Học", "Tầng 1, Tòa A", "Kho lưu giáo trình khoa học");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Warehouse);
        Assert.Equal("KHO-01", result.Warehouse.Code);
        Assert.Equal("Kho Sách Khoa Học", result.Warehouse.Name);
        Assert.Equal(WarehouseStatus.Active, result.Warehouse.Status);

        var saved = await db.Warehouses.FirstOrDefaultAsync(w => w.Code == "KHO-01");
        Assert.NotNull(saved);
        Assert.Equal("Kho Sách Khoa Học", saved.Name);
    }

    [Fact]
    public async Task Test2_ThemKeThuocKho_ThanhCong()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var warehouseService = new WarehouseService(db);
        var shelfService = new ShelfService(db);

        var whResult = await warehouseService.CreateAsync("KHO-A", "Kho A", "Tầng 1", null);
        Assert.True(whResult.IsSuccess);
        var warehouseId = whResult.Warehouse!.Id;

        // Act
        var shelfResult = await shelfService.CreateAsync(warehouseId, "KE-01", "Kệ Sách Toán Học", "Ngăn 1-3");

        // Assert
        Assert.True(shelfResult.IsSuccess);
        Assert.NotNull(shelfResult.Shelf);
        Assert.Equal("KE-01", shelfResult.Shelf.Code);
        Assert.Equal("Kệ Sách Toán Học", shelfResult.Shelf.Name);
        Assert.Equal(warehouseId, shelfResult.Shelf.WarehouseId);

        var savedShelf = await db.Shelves.Include(s => s.Warehouse).FirstOrDefaultAsync(s => s.WarehouseId == warehouseId && s.Code == "KE-01");
        Assert.NotNull(savedShelf);
        Assert.Equal("Kệ Sách Toán Học", savedShelf.Name);
        Assert.Equal("KHO-A", savedShelf.Warehouse!.Code);
    }

    [Fact]
    public async Task Test3_ThemHaiKeCungMa_TrongMotKho_ThatBaiVaBaoLoi()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var warehouseService = new WarehouseService(db);
        var shelfService = new ShelfService(db);

        var whResult = await warehouseService.CreateAsync("KHO-B", "Kho B", "Tầng 2", null);
        var warehouseId = whResult.Warehouse!.Id;

        // Thêm kệ thứ nhất với mã KE-01
        var shelf1Result = await shelfService.CreateAsync(warehouseId, "KE-01", "Kệ Văn Học 1", "Tầng 1");
        Assert.True(shelf1Result.IsSuccess);

        // Act: Thêm kệ thứ hai CÙNG mã KE-01 trong CÙNG kho B
        var shelf2Result = await shelfService.CreateAsync(warehouseId, "KE-01", "Kệ Văn Học 2", "Tầng 2");

        // Assert
        Assert.False(shelf2Result.IsSuccess);
        Assert.Null(shelf2Result.Shelf);
        Assert.NotNull(shelf2Result.ErrorMessage);
        Assert.Contains("KE-01", shelf2Result.ErrorMessage);
        Assert.Contains("đã tồn tại trong kho này", shelf2Result.ErrorMessage);

        // Kiểm tra trong DB chỉ có đúng 1 kệ với mã KE-01 thuộc kho B
        var count = await db.Shelves.CountAsync(s => s.WarehouseId == warehouseId && s.Code == "KE-01");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Test4_DungCungMaKe_OHaiKhoKhacNhau_ThanhCong()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var warehouseService = new WarehouseService(db);
        var shelfService = new ShelfService(db);

        var wh1 = (await warehouseService.CreateAsync("KHO-01", "Kho Trung Tâm", null, null)).Warehouse!;
        var wh2 = (await warehouseService.CreateAsync("KHO-02", "Kho Chi Nhánh", null, null)).Warehouse!;

        // Act: Dùng CÙNG mã kệ "KE-VIP" ở HAI kho khác nhau
        var shelfAtWh1 = await shelfService.CreateAsync(wh1.Id, "KE-VIP", "Kệ VIP Kho 1", "Khu đặc biệt");
        var shelfAtWh2 = await shelfService.CreateAsync(wh2.Id, "KE-VIP", "Kệ VIP Kho 2", "Khu đặc biệt chi nhánh");

        // Assert: Cả 2 đều thành công
        Assert.True(shelfAtWh1.IsSuccess, "Tạo kệ ở kho 1 phải thành công");
        Assert.True(shelfAtWh2.IsSuccess, "Tạo kệ cùng mã ở kho 2 khác kho 1 cũng phải thành công");

        Assert.Equal("KE-VIP", shelfAtWh1.Shelf!.Code);
        Assert.Equal("KE-VIP", shelfAtWh2.Shelf!.Code);
        Assert.NotEqual(shelfAtWh1.Shelf.WarehouseId, shelfAtWh2.Shelf.WarehouseId);

        var allVipShelves = await db.Shelves.Where(s => s.Code == "KE-VIP").ToListAsync();
        Assert.Equal(2, allVipShelves.Count);
    }

    [Fact]
    public async Task Test5_CapNhatKe_TrungMaTrongCungKho_ThatBai()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var warehouseService = new WarehouseService(db);
        var shelfService = new ShelfService(db);

        var wh = (await warehouseService.CreateAsync("KHO-EDIT", "Kho Edit", null, null)).Warehouse!;
        var s1 = (await shelfService.CreateAsync(wh.Id, "KE-01", "Kệ 1", null)).Shelf!;
        var s2 = (await shelfService.CreateAsync(wh.Id, "KE-02", "Kệ 2", null)).Shelf!;

        // Act: Đổi mã s2 thành KE-01 (đã có ở s1 trong cùng kho)
        var updateResult = await shelfService.UpdateAsync(s2.Id, wh.Id, "KE-01", "Kệ 2 Đổi Tên", null);

        // Assert
        Assert.False(updateResult.IsSuccess);
        Assert.Contains("KE-01", updateResult.ErrorMessage);
        Assert.Contains("đã tồn tại trong kho này", updateResult.ErrorMessage);
    }

    [Fact]
    public async Task Test6_CapNhatKe_CungKhoGiuNguyenMa_ThanhCong()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var warehouseService = new WarehouseService(db);
        var shelfService = new ShelfService(db);

        var wh = (await warehouseService.CreateAsync("KHO-KEEP", "Kho Keep", null, null)).Warehouse!;
        var s1 = (await shelfService.CreateAsync(wh.Id, "KE-01", "Kệ Cũ", null)).Shelf!;

        // Act: Sửa tên/mô tả nhưng giữ nguyên mã KE-01 của chính nó
        var updateResult = await shelfService.UpdateAsync(s1.Id, wh.Id, "KE-01", "Kệ Mới Đã Cập Nhật", "Mô tả mới");

        // Assert
        Assert.True(updateResult.IsSuccess);
        Assert.Equal("Kệ Mới Đã Cập Nhật", updateResult.Shelf!.Name);
        Assert.Equal("KE-01", updateResult.Shelf.Code);
    }

    [Fact]
    public async Task Test7_XoaKho_KhiConKeTrucThuoc_BiChan()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var warehouseService = new WarehouseService(db);
        var shelfService = new ShelfService(db);

        var wh = (await warehouseService.CreateAsync("KHO-DEL", "Kho Thử Xóa", null, null)).Warehouse!;
        await shelfService.CreateAsync(wh.Id, "KE-DEL-01", "Kệ 1", null);

        // Act: Thử xóa kho khi vẫn còn kệ trực thuộc
        var deleteResult = await warehouseService.DeleteAsync(wh.Id);

        // Assert: Phải bị chặn
        Assert.False(deleteResult.IsSuccess);
        Assert.True(deleteResult.HasLinkedShelves);
        Assert.Contains("Không thể xóa kho vì còn kệ sách trực thuộc", deleteResult.ErrorMessage);

        // Kiểm tra kho vẫn còn trong DB
        var whExists = await db.Warehouses.AnyAsync(w => w.Id == wh.Id);
        Assert.True(whExists);
    }

    [Fact]
    public async Task Test8_ThemKho_TrungMaKho_ThatBaiVaBaoLoi()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var warehouseService = new WarehouseService(db);

        await warehouseService.CreateAsync("KHO-DUP", "Kho Ban Đầu", null, null);

        // Act: Thêm kho thứ 2 trùng mã KHO-DUP
        var result = await warehouseService.CreateAsync("KHO-DUP", "Kho Trùng Mã", null, null);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("KHO-DUP", result.ErrorMessage);
        Assert.Contains("đã tồn tại trong hệ thống", result.ErrorMessage);
    }

    [Fact]
    public async Task Test9_WarehouseController_ApiCrud_HoatDongDung()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var warehouseService = new WarehouseService(db);
        var shelfService = new ShelfService(db);
        var controller = new WarehouseController(warehouseService, shelfService, db);

        // 1. Tạo kho qua API
        var createResult = await controller.CreateApi(new CreateWarehouseDto
        {
            Code = "API-KHO-1",
            Name = "Kho API",
            Address = "123 Đường Sách",
            Description = "Mô tả kho API"
        });
        var created = Assert.IsType<CreatedResult>(createResult);
        Assert.NotNull(created.Value);

        // 2. Thử tạo kho trùng mã qua API -> 400 BadRequest
        var dupResult = await controller.CreateApi(new CreateWarehouseDto
        {
            Code = "API-KHO-1",
            Name = "Kho API Trùng",
            Address = null,
            Description = null
        });
        Assert.IsType<BadRequestObjectResult>(dupResult);

        // 3. Lấy danh sách kho qua API
        var getListResult = await controller.GetAllApi();
        var okList = Assert.IsType<OkObjectResult>(getListResult);
        Assert.NotNull(okList.Value);
    }

    [Fact]
    public async Task Test10_ShelfController_ApiCrud_HoatDongDung()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var warehouseService = new WarehouseService(db);
        var shelfService = new ShelfService(db);
        var shelfController = new ShelfController(shelfService, warehouseService, db);

        var wh1 = (await warehouseService.CreateAsync("WH-A", "Kho A", null, null)).Warehouse!;
        var wh2 = (await warehouseService.CreateAsync("WH-B", "Kho B", null, null)).Warehouse!;

        // 1. Tạo kệ tại Kho A qua API
        var res1 = await shelfController.CreateApi(new CreateShelfDto
        {
            WarehouseId = wh1.Id,
            Code = "KE-X",
            Name = "Kệ X Kho A",
            Description = "Mô tả X"
        });
        Assert.IsType<CreatedResult>(res1);

        // 2. Tạo kệ CÙNG mã KE-X tại Kho A qua API -> 400 BadRequest
        var dupRes = await shelfController.CreateApi(new CreateShelfDto
        {
            WarehouseId = wh1.Id,
            Code = "KE-X",
            Name = "Kệ X Trùng ở Kho A",
            Description = null
        });
        var badRequest = Assert.IsType<BadRequestObjectResult>(dupRes);
        Assert.NotNull(badRequest.Value);

        // 3. Tạo kệ CÙNG mã KE-X tại Kho B qua API -> Thành công!
        var res2 = await shelfController.CreateApi(new CreateShelfDto
        {
            WarehouseId = wh2.Id,
            Code = "KE-X",
            Name = "Kệ X Kho B",
            Description = "Mô tả X ở kho B"
        });
        Assert.IsType<CreatedResult>(res2);

        // 4. Lấy kệ theo Kho A qua API
        var getByWh1 = await shelfController.GetAllApi(wh1.Id);
        var okList1 = Assert.IsType<OkObjectResult>(getByWh1);
        Assert.NotNull(okList1.Value);
    }

    [Fact]
    public async Task Test11_ShelfController_XemChiTietKe_ThanhCong()
    {
        using var db = CreateInMemoryDbContext();
        var warehouseService = new WarehouseService(db);
        var shelfService = new ShelfService(db);
        var controller = new ShelfController(shelfService, warehouseService, db);

        var warehouse = (await warehouseService.CreateAsync("KHO-DETAIL", "Kho chi tiết", null, null)).Warehouse!;
        var shelf = (await shelfService.CreateAsync(warehouse.Id, "KE-DETAIL", "Kệ chi tiết", "Mô tả")).Shelf!;

        var result = await controller.Details(shelf.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<Shelf>(view.Model);
        Assert.Equal(shelf.Id, model.Id);
        Assert.Equal(warehouse.Id, model.WarehouseId);
        Assert.NotNull(model.Warehouse);
    }
}
