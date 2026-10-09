using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

public sealed class ShelfController(
    IShelfService shelfService,
    IWarehouseService warehouseService) : Controller
{
    // ==========================================
    // MVC VIEW ACTIONS
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> Index(int? warehouseId, CancellationToken ct = default)
    {
        var shelves = await shelfService.GetAllAsync(warehouseId, ct);
        var warehouses = await warehouseService.GetAllAsync(ct);

        return View(new ShelfIndexViewModel
        {
            Shelves = shelves,
            Warehouses = warehouses,
            SelectedWarehouseId = warehouseId,
            NewShelf = new CreateShelfViewModel
            {
                WarehouseId = warehouseId ?? (warehouses.Count > 0 ? warehouses[0].Id : 0)
            }
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateShelfViewModel newShelf, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu không hợp lệ.";
            return RedirectToAction(nameof(Index), new { warehouseId = newShelf.WarehouseId });
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
            return RedirectToAction(nameof(Index), new { warehouseId = newShelf.WarehouseId });
        }

        TempData["SuccessMessage"] = $"Đã thêm kệ ‘{outcome.Shelf!.Name}’ (Mã: {outcome.Shelf.Code}) thành công.";
        return RedirectToAction(nameof(Index), new { warehouseId = newShelf.WarehouseId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditShelfViewModel model, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu chỉnh sửa không hợp lệ.";
            return RedirectToAction(nameof(Index), new { warehouseId = model.WarehouseId });
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

        return RedirectToAction(nameof(Index), new { warehouseId = model.WarehouseId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id, int? warehouseId, CancellationToken ct = default)
    {
        var outcome = await shelfService.DeactivateAsync(id, ct);
        TempData[outcome.IsSuccess ? "SuccessMessage" : "ErrorMessage"] =
            outcome.IsSuccess ? "Đã chuyển kệ sang trạng thái ngừng sử dụng." : outcome.ErrorMessage;
        return RedirectToAction(nameof(Index), new { warehouseId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, int? warehouseId, CancellationToken ct = default)
    {
        var outcome = await shelfService.DeleteAsync(id, ct);
        TempData[outcome.IsSuccess ? "SuccessMessage" : "ErrorMessage"] =
            outcome.IsSuccess ? "Đã xóa kệ sách thành công." : outcome.ErrorMessage;
        return RedirectToAction(nameof(Index), new { warehouseId });
    }

    // ==========================================
    // REST API ENDPOINTS
    // ==========================================

    [HttpGet("api/shelves")]
    public async Task<IActionResult> GetAllApi([FromQuery] int? warehouseId, CancellationToken ct = default)
    {
        var shelves = await shelfService.GetAllAsync(warehouseId, ct);
        var result = shelves.Select(s => new
        {
            s.Id,
            s.WarehouseId,
            WarehouseCode = s.Warehouse?.Code,
            WarehouseName = s.Warehouse?.Name,
            s.Code,
            s.Name,
            s.Description,
            s.Status,
            s.CreatedAtUtc
        });
        return Ok(result);
    }

    [HttpGet("api/shelves/{id:int}")]
    public async Task<IActionResult> GetByIdApi(int id, CancellationToken ct = default)
    {
        var shelf = await shelfService.GetByIdAsync(id, ct);
        if (shelf == null) return NotFound(new { message = "Không tìm thấy kệ sách." });

        return Ok(new
        {
            shelf.Id,
            shelf.WarehouseId,
            WarehouseCode = shelf.Warehouse?.Code,
            WarehouseName = shelf.Warehouse?.Name,
            shelf.Code,
            shelf.Name,
            shelf.Description,
            shelf.Status,
            shelf.CreatedAtUtc
        });
    }

    [HttpPost("api/shelves")]
    public async Task<IActionResult> CreateApi([FromBody] CreateShelfDto? dto, CancellationToken ct = default)
    {
        if (dto == null) return BadRequest(new { message = "Dữ liệu không được để trống." });
        if (!TryValidate(dto, out var error)) return BadRequest(new { message = error });

        var outcome = await shelfService.CreateAsync(dto.WarehouseId, dto.Code, dto.Name, dto.Description, ct);
        if (!outcome.IsSuccess)
        {
            return BadRequest(new { message = outcome.ErrorMessage });
        }

        return Created($"/api/shelves/{outcome.Shelf!.Id}", new
        {
            message = "Tạo kệ thành công.",
            outcome.Shelf.Id,
            outcome.Shelf.WarehouseId,
            outcome.Shelf.Code,
            outcome.Shelf.Name,
            outcome.Shelf.Description,
            outcome.Shelf.Status
        });
    }

    [HttpPut("api/shelves/{id:int}")]
    public async Task<IActionResult> UpdateApi(int id, [FromBody] UpdateShelfDto? dto, CancellationToken ct = default)
    {
        if (dto == null) return BadRequest(new { message = "Dữ liệu không được để trống." });
        if (!TryValidate(dto, out var error)) return BadRequest(new { message = error });

        var outcome = await shelfService.UpdateAsync(id, dto.WarehouseId, dto.Code, dto.Name, dto.Description, dto.Status, ct);
        if (!outcome.IsSuccess)
        {
            return outcome.ErrorMessage == "Không tìm thấy kệ sách."
                ? NotFound(new { message = outcome.ErrorMessage })
                : BadRequest(new { message = outcome.ErrorMessage });
        }

        return Ok(new
        {
            message = "Cập nhật kệ thành công.",
            outcome.Shelf!.Id,
            outcome.Shelf.WarehouseId,
            outcome.Shelf.Code,
            outcome.Shelf.Name,
            outcome.Shelf.Description,
            outcome.Shelf.Status
        });
    }

    [HttpDelete("api/shelves/{id:int}")]
    public async Task<IActionResult> DeleteApi(int id, CancellationToken ct = default)
    {
        var outcome = await shelfService.DeleteAsync(id, ct);
        if (!outcome.IsSuccess)
        {
            return NotFound(new { message = outcome.ErrorMessage });
        }

        return Ok(new { message = "Xóa kệ sách thành công." });
    }

    private static bool TryValidate(object dto, out string? errorMessage)
    {
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(dto, new ValidationContext(dto), validationResults, true);
        errorMessage = isValid ? null : validationResults.FirstOrDefault()?.ErrorMessage ?? "Dữ liệu không hợp lệ.";
        return isValid;
    }
}
