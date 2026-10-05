using Project.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

[StaffOnly(AccountRoles.SystemAdmin, AccountRoles.LibraryManager)]
public sealed class CategoryController(ICategoryService categories, ApplicationDbContext dbContext) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct = default)
    {
        if (!await IsLibrarianSignedInAsync(ct)) return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Index)) });
        return View(await GetManagementModel(ct));
    }

    private async Task<bool> IsLibrarianSignedInAsync(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue("admin_refresh", out var token) || string.IsNullOrWhiteSpace(token)) return false;
        var hash = TokenService.HashRefreshToken(token);
        return await dbContext.RefreshTokens.AnyAsync(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > DateTime.UtcNow && item.AdminAccount.IsActive && (item.AdminAccount.Role == AccountRoles.SystemAdmin || item.AdminAccount.Role == AccountRoles.LibraryManager), ct);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CategoryInputViewModel model, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return await IndexWithError(model, ct);
        var result = await categories.CreateAsync(model.Name, model.ParentId, ct);
        SetResult(result, $"Đã thêm thể loại ‘{result.Category?.Name}’.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CategoryInputViewModel model, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) { TempData["ErrorMessage"] = "Tên thể loại không hợp lệ."; return RedirectToAction(nameof(Index)); }
        var result = await categories.UpdateAsync(id, model.Name, model.ParentId, parentIdSpecified: true, cancellationToken: ct);
        SetResult(result, "Đã cập nhật thể loại.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct = default)
    {
        var result = await categories.DeactivateAsync(id, ct);
        SetResult(result, "Đã chuyển thể loại sang trạng thái ngừng sử dụng.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        var result = await categories.DeleteAsync(id, ct);
        SetResult(result, "Đã xóa thể loại.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("api/categories")]
    public async Task<IActionResult> GetAllApi(CancellationToken ct = default) => Ok(BuildTree(await categories.GetAllAsync(ct)));

    [HttpGet("api/categories/active")]
    public async Task<IActionResult> GetActiveApi(CancellationToken ct = default) => Ok(BuildTree(await categories.GetActiveAsync(ct)));

    [HttpPost("api/categories")]
    public async Task<IActionResult> CreateApi([FromBody] CategoryInputViewModel? model, CancellationToken ct = default)
    {
        if (model == null || !TryValidateModel(model)) return ValidationProblem(ModelState);
        var result = await categories.CreateAsync(model.Name, model.ParentId, ct);
        if (!result.IsSuccess) return BadRequest(new { message = result.ErrorMessage });
        return Created($"/api/categories/{result.Category!.Id}", new { id = result.Category.Id, name = result.Category.Name, status = result.Category.Status, parentId = result.Category.ParentId });
    }

    [HttpPut("api/categories/{id:int}")]
    public async Task<IActionResult> UpdateApi(int id, [FromBody] CategoryInputViewModel? model, CancellationToken ct = default)
    {
        if (model == null || !TryValidateModel(model)) return ValidationProblem(ModelState);
        var result = await categories.UpdateAsync(id, model.Name, model.ParentId, model.ParentIdSpecified, ct);
        if (!result.IsSuccess) return result.ErrorMessage == "Không tìm thấy thể loại." ? NotFound(new { message = result.ErrorMessage }) : BadRequest(new { message = result.ErrorMessage });
        return Ok(new { message = "Cập nhật thể loại thành công.", id, name = result.Category!.Name, status = result.Category.Status, parentId = result.Category.ParentId });
    }

    [HttpPost("api/categories/{id:int}/deactivate")]
    public async Task<IActionResult> DeactivateApi(int id, CancellationToken ct = default)
    {
        var result = await categories.DeactivateAsync(id, ct);
        if (!result.IsSuccess) return NotFound(new { message = result.ErrorMessage });
        return Ok(new { message = "Đã ngừng sử dụng thể loại.", id, status = result.Category!.Status });
    }

    [HttpDelete("api/categories/{id:int}")]
    public async Task<IActionResult> DeleteApi(int id, CancellationToken ct = default)
    {
        var result = await categories.DeleteAsync(id, ct);
        if (!result.IsSuccess) return result.ErrorMessage == "Không tìm thấy thể loại." ? NotFound(new { message = result.ErrorMessage }) : Conflict(new { message = result.ErrorMessage });
        return Ok(new { message = "Xóa thể loại thành công." });
    }

    private async Task<IActionResult> IndexWithError(CategoryInputViewModel model, CancellationToken ct)
    {
        TempData["ErrorMessage"] = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu không hợp lệ.";
        return View(nameof(Index), await GetManagementModel(ct));
    }

    private async Task<CategoryManagementViewModel> GetManagementModel(CancellationToken ct)
    {
        var all = await categories.GetAllAsync(ct);
        return new CategoryManagementViewModel
        {
            Categories = all,
            ParentOptions = all.Where(c => c.ParentId == null && c.Status == CategoryStatus.Active).ToList()
        };
    }

    private static IReadOnlyList<CategoryTreeViewModel> BuildTree(IReadOnlyList<Category> categories)
    {
        var nodes = categories.ToDictionary(c => c.Id, c => new CategoryTreeViewModel
        {
            Id = c.Id,
            Name = c.Name,
            Status = c.Status
        });
        foreach (var category in categories)
        {
            if (category.ParentId is int parentId && nodes.TryGetValue(parentId, out var parent))
                parent.Children.Add(nodes[category.Id]);
        }
        return categories.Where(c => c.ParentId == null || !nodes.ContainsKey(c.ParentId.Value))
            .Select(c => nodes[c.Id]).OrderBy(c => c.Name).ToList();
    }

    private void SetResult(CategoryOutcome result, string success)
    {
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess ? success : result.ErrorMessage;
    }
}
