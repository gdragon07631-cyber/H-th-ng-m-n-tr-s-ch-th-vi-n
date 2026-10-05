using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;
using Project.Services;

namespace Project.Controllers;

/// <summary>Quản trị hệ thống xem và sửa chính sách mượn.</summary>
public sealed class LoanPolicyController(
    ApplicationDbContext dbContext,
    IAuditLogService auditLogService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        if (await GetSignedInAdminAsync(cancellationToken) is null)
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Index)) });

        var policy = await dbContext.LoanPolicies.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == LoanPolicy.SingletonId, cancellationToken);
        return View(new LoanPolicyViewModel
        {
            LoanDays = policy?.LoanDays ?? LoanPolicy.DefaultLoanDays,
            UpdatedAtUtc = policy?.UpdatedAtUtc
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(LoanPolicyViewModel model, CancellationToken cancellationToken = default)
    {
        var admin = await GetSignedInAdminAsync(cancellationToken);
        if (admin is null)
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Index)) });

        var policy = await dbContext.LoanPolicies
            .SingleOrDefaultAsync(item => item.Id == LoanPolicy.SingletonId, cancellationToken);
        if (!ModelState.IsValid)
        {
            model.UpdatedAtUtc = policy?.UpdatedAtUtc;
            return View(model);
        }

        if (policy is null)
        {
            policy = new LoanPolicy { Id = LoanPolicy.SingletonId };
            dbContext.LoanPolicies.Add(policy);
        }

        var previousLoanDays = policy.LoanDays;
        if (policy.LoanDays == model.LoanDays && dbContext.Entry(policy).State != EntityState.Added)
        {
            TempData["SuccessMessage"] = "Chính sách mượn không thay đổi.";
            return RedirectToAction(nameof(Index));
        }

        policy.LoanDays = model.LoanDays;
        policy.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditLogService.WriteAsync(
            admin.Email,
            AuditActions.UpdateLoanPolicy,
            $"Chính sách mượn: số ngày mượn {previousLoanDays} → {policy.LoanDays}",
            AuditLogService.ClientIp(HttpContext),
            cancellationToken);

        TempData["SuccessMessage"] = "Đã cập nhật chính sách mượn.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<AdminAccount?> GetSignedInAdminAsync(CancellationToken cancellationToken)
    {
        var staff = await auditLogService.GetSignedInStaffAsync(Request, cancellationToken);
        return staff?.Role == AccountRoles.SystemAdmin ? staff : null;
    }
}
