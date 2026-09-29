using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

public sealed class WarehouseController(
    IWarehouseService warehouseService,
    IShelfService shelfService,
    ApplicationDbContext dbContext) : Controller
{
    // ==========================================
    // MVC VIEW ACTIONS
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct = default)
    {
        if (!await IsLibrarianSignedInAsync(ct))
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Index)) });

        var warehouses = await warehouseService.GetAllAsync(ct);
        return View(new WarehouseIndexViewModel
        {
            Warehouses = warehouses,
            NewWarehouse = new CreateWarehouseViewModel()
        });
    }

    private async Task<bool> IsLibrarianSignedInAsync(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue("admin_refresh", out var token) || string.IsNullOrWhiteSpace(token)) return false;
        var hash = TokenService.HashRefreshToken(token);
        return await dbContext.RefreshTokens.AnyAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTime.UtcNow && item.AdminAccount.IsActive && (item.AdminAccount.Role == AccountRoles.SystemAdmin || item.AdminAccount.Role == AccountRoles.LibraryManager), ct);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateWarehouseViewModel newWarehouse, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            var warehouses = await warehouseService.GetAllAsync(ct);
            TempData["ErrorMessage"] = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu không hợp lệ.";
            return View(nameof(Index), new WarehouseIndexViewModel
            {
                Warehouses = warehouses,
                NewWarehouse = newWarehouse
            });
        }

        var outcome = await warehouseService.CreateAsync(
            newWarehouse.Code,
            newWarehouse.Name,
            newWarehouse.Address,
            newWarehouse.Description,
            ct);

        if (!outcome.IsSuccess)
        {
            var warehouses = await warehouseService.GetAllAsync(ct);
            TempData["ErrorMessage"] = outcome.ErrorMessage;
            return View(nameof(Index), new WarehouseIndexViewModel
            {
                Warehouses = warehouses,
                NewWarehouse = newWarehouse
            });
        }

        TempData["SuccessMessage"] = $"Đã thêm kho ‘{outcome.Warehouse!.Name}’ (Mã: {outcome.Warehouse.Code}) thành công.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditWarehouseViewModel model, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu chỉnh sửa không hợp lệ.";
            return RedirectToAction(nameof(Index));
        }

        var outcome = await warehouseService.UpdateAsync(
            model.Id,
            model.Code,
            model.Name,
            model.Address,
            model.Description,
            model.Status,
            ct);

        TempData[outcome.IsSuccess ? "SuccessMessage" : "ErrorMessage"] =
            outcome.IsSuccess ? $"Đã cập nhật kho ‘{outcome.Warehouse!.Name}’." : outcome.ErrorMessage;

        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct = default)
    {
        var outcome = await warehouseService.DeactivateAsync(id, ct);
        TempData[outcome.IsSuccess ? "SuccessMessage" : "ErrorMessage"] =
            outcome.IsSuccess ? "Đã chuyển kho sang trạng thái ngừng sử dụng." : outcome.ErrorMessage;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        var outcome = await warehouseService.DeleteAsync(id, ct);
        TempData[outcome.IsSuccess ? "SuccessMessage" : "ErrorMessage"] =
            outcome.IsSuccess ? "Đã xóa kho thành công." : outcome.ErrorMessage;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct = default)
    {
        var warehouse = await warehouseService.GetByIdAsync(id, includeShelves: true, ct);
        if (warehouse == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy kho yêu cầu.";
            return RedirectToAction(nameof(Index));
        }

        return View(new WarehouseDetailsViewModel
        {
            Warehouse = warehouse,
            Shelves = warehouse.Shelves.ToList(),
            NewShelf = new CreateShelfViewModel { WarehouseId = warehouse.Id }
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateShelf(CreateShelfViewModel newShelf, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu kệ không hợp lệ.";
            return RedirectToAction(nameof(Details), new { id = newShelf.WarehouseId });
        }

        var outcome = await shelfService.CreateAsync(
            newShelf.WarehouseId,
            newShelf.Code,
            newShelf.Name,
            newShelf.Description,
            ct);

        if (!outcome.IsSuccess)
        {
            TempData["ErrorMessage"] = outcome.ErrorMessage;
            return RedirectToAction(nameof(Details), new { id = newShelf.WarehouseId });
        }

        TempData["SuccessMessage"] = $"Đã thêm kệ ‘{outcome.Shelf!.Name}’ (Mã: {outcome.Shelf.Code}) vào kho thành công.";
        return RedirectToAction(nameof(Details), new { id = newShelf.WarehouseId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditShelf(EditShelfViewModel model, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu chỉnh sửa kệ không hợp lệ.";
            return RedirectToAction(nameof(Details), new { id = model.WarehouseId });
        }

        var outcome = await shelfService.UpdateAsync(
            model.Id,
            model.WarehouseId,
            model.Code,
            model.Name,
            model.Description,
            model.Status,
            ct);

        TempData[outcome.IsSuccess ? "SuccessMessage" : "ErrorMessage"] =
            outcome.IsSuccess ? $"Đã cập nhật kệ ‘{outcome.Shelf!.Name}’." : outcome.ErrorMessage;

        return RedirectToAction(nameof(Details), new { id = model.WarehouseId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteShelf(int id, int warehouseId, CancellationToken ct = default)
    {
        var outcome = await shelfService.DeleteAsync(id, ct);
        TempData[outcome.IsSuccess ? "SuccessMessage" : "ErrorMessage"] =
            outcome.IsSuccess ? "Đã xóa kệ sách thành công." : outcome.ErrorMessage;
        return RedirectToAction(nameof(Details), new { id = warehouseId });
    }

    // ==========================================
    // REST API ENDPOINTS
    // ==========================================

    [HttpGet("api/warehouses")]
    public async Task<IActionResult> GetAllApi(CancellationToken ct = default)
    {
        var warehouses = await warehouseService.GetAllAsync(ct);
        var result = warehouses.Select(w => new
        {
            w.Id,
            w.Code,
            w.Name,
            w.Address,
            w.Description,
            w.Status,
            w.CreatedAtUtc,
            ShelfCount = w.Shelves.Count
        });
        return Ok(result);
    }

    [HttpGet("api/warehouses/{id:int}")]
    public async Task<IActionResult> GetByIdApi(int id, CancellationToken ct = default)
    {
        var warehouse = await warehouseService.GetByIdAsync(id, includeShelves: true, ct);
        if (warehouse == null) return NotFound(new { message = "Không tìm thấy kho." });

        return Ok(new
        {
            warehouse.Id,
            warehouse.Code,
            warehouse.Name,
            warehouse.Address,
            warehouse.Description,
            warehouse.Status,
            warehouse.CreatedAtUtc,
            Shelves = warehouse.Shelves.Select(s => new
            {
                s.Id,
                s.Code,
                s.Name,
                s.Description,
                s.Status,
                s.CreatedAtUtc
            })
        });
    }

    [HttpPost("api/warehouses")]
    public async Task<IActionResult> CreateApi([FromBody] CreateWarehouseDto? dto, CancellationToken ct = default)
    {
        if (dto == null) return BadRequest(new { message = "Dữ liệu không được để trống." });
        if (!TryValidate(dto, out var error)) return BadRequest(new { message = error });

        var outcome = await warehouseService.CreateAsync(dto.Code, dto.Name, dto.Address, dto.Description, ct);
        if (!outcome.IsSuccess)
        {
            return BadRequest(new { message = outcome.ErrorMessage });
        }

        return Created($"/api/warehouses/{outcome.Warehouse!.Id}", new
        {
            message = "Tạo kho thành công.",
            outcome.Warehouse.Id,
            outcome.Warehouse.Code,
            outcome.Warehouse.Name,
            outcome.Warehouse.Address,
            outcome.Warehouse.Description,
            outcome.Warehouse.Status
        });
    }

    [HttpPut("api/warehouses/{id:int}")]
    public async Task<IActionResult> UpdateApi(int id, [FromBody] UpdateWarehouseDto? dto, CancellationToken ct = default)
    {
        if (dto == null) return BadRequest(new { message = "Dữ liệu không được để trống." });
        if (!TryValidate(dto, out var error)) return BadRequest(new { message = error });

        var outcome = await warehouseService.UpdateAsync(id, dto.Code, dto.Name, dto.Address, dto.Description, dto.Status, ct);
        if (!outcome.IsSuccess)
        {
            return outcome.ErrorMessage == "Không tìm thấy kho."
                ? NotFound(new { message = outcome.ErrorMessage })
                : BadRequest(new { message = outcome.ErrorMessage });
        }

        return Ok(new
        {
            message = "Cập nhật kho thành công.",
            outcome.Warehouse!.Id,
            outcome.Warehouse.Code,
            outcome.Warehouse.Name,
            outcome.Warehouse.Address,
            outcome.Warehouse.Description,
            outcome.Warehouse.Status
        });
    }

    [HttpDelete("api/warehouses/{id:int}")]
    public async Task<IActionResult> DeleteApi(int id, CancellationToken ct = default)
    {
        var outcome = await warehouseService.DeleteAsync(id, ct);
        if (!outcome.IsSuccess)
        {
            if (outcome.ErrorMessage == "Không tìm thấy kho.")
                return NotFound(new { message = outcome.ErrorMessage });
            return Conflict(new { message = outcome.ErrorMessage });
        }

        return Ok(new { message = "Xóa kho thành công." });
    }

    [HttpGet("api/warehouses/{warehouseId:int}/shelves")]
    public async Task<IActionResult> GetShelvesByWarehouseApi(int warehouseId, CancellationToken ct = default)
    {
        var warehouse = await warehouseService.GetByIdAsync(warehouseId, includeShelves: true, ct);
        if (warehouse == null) return NotFound(new { message = "Không tìm thấy kho." });

        var shelves = warehouse.Shelves.Select(s => new
        {
            s.Id,
            s.WarehouseId,
            WarehouseName = warehouse.Name,
            s.Code,
            s.Name,
            s.Description,
            s.Status,
            s.CreatedAtUtc
        });
        return Ok(shelves);
    }

    private static bool TryValidate(object dto, out string? errorMessage)
    {
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(dto, new ValidationContext(dto), validationResults, true);
        errorMessage = isValid ? null : validationResults.FirstOrDefault()?.ErrorMessage ?? "Dữ liệu không hợp lệ.";
        return isValid;
    }
}
