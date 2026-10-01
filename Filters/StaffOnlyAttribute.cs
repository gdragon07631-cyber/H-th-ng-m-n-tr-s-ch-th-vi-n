using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Project.Services;

namespace Project.Filters;

/// <summary>
/// Requires a signed-in staff account (cookie admin_refresh) with one of the given roles for every action of the
/// controller — page posts and REST APIs alike — unless the action is marked <see cref="PublicActionAttribute"/>.
/// Pages are redirected to the staff login; API calls get 401/403 JSON.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class StaffOnlyAttribute(params string[] roles) : Attribute, IAsyncActionFilter
{
    public IReadOnlyList<string> Roles { get; } = roles;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor.EndpointMetadata.OfType<PublicActionAttribute>().Any())
        {
            await next();
            return;
        }

        var request = context.HttpContext.Request;
        var auditLogService = context.HttpContext.RequestServices.GetRequiredService<IAuditLogService>();
        var staff = await auditLogService.GetSignedInStaffAsync(request, context.HttpContext.RequestAborted);
        if (staff is not null && Roles.Contains(staff.Role))
        {
            await next();
            return;
        }

        if (IsApiRequest(request))
        {
            context.Result = staff is null
                ? new UnauthorizedObjectResult(new { message = "Vui lòng đăng nhập bằng tài khoản nhân sự." })
                : new ObjectResult(new { message = "Tài khoản của bạn không có quyền thực hiện thao tác này." })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            return;
        }

        var returnUrl = HttpMethods.IsGet(request.Method) ? $"{request.PathBase}{request.Path}{request.QueryString}" : null;
        context.Result = new RedirectToActionResult("Login", "Account", returnUrl is null ? null : new { returnUrl });
    }

    private static bool IsApiRequest(HttpRequest request) =>
        request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase) ||
        request.Headers.Accept.Any(value => value?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);
}

/// <summary>Opts an action out of <see cref="StaffOnlyAttribute"/> (public pages and reader actions).</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PublicActionAttribute : Attribute;
