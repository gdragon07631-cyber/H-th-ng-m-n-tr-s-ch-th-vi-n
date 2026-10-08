using System.Net;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class AuditLogService(
    ApplicationDbContext dbContext,
    ILogger<AuditLogService> logger) : IAuditLogService
{
    public const string UnknownIpAddress = "Không xác định";

    public async Task WriteAsync(string actor, string action, string target, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var entry = Create(actor, action, target, ipAddress);
        dbContext.AuditLogs.Add(entry);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            dbContext.Entry(entry).State = EntityState.Detached;
            logger.LogError(exception, "Không thể ghi nhật ký hoạt động {Action} cho {Target}.", action, target);
        }
    }

    public Task<IReadOnlyList<AuditLog>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) =>
        SearchAsync(new AuditLogFilter(), limit, cancellationToken);

    public async Task<IReadOnlyList<AuditLog>> SearchAsync(AuditLogFilter filter, int limit, CancellationToken cancellationToken = default)
    {
        IQueryable<AuditLog> query = dbContext.AuditLogs.AsNoTracking();
        if (filter.FromDate is { } fromDate)
        {
            var fromUtc = StartOfLocalDayUtc(fromDate);
            query = query.Where(log => log.OccurredAtUtc >= fromUtc);
        }
        if (filter.ToDate is { } toDate)
        {
            var endUtc = StartOfLocalDayUtc(toDate.AddDays(1));
            query = query.Where(log => log.OccurredAtUtc < endUtc);
        }
        if (!string.IsNullOrWhiteSpace(filter.Actor))
        {
            var actor = filter.Actor.Trim();
            query = query.Where(log => log.Actor == actor);
        }
        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            var action = filter.Action.Trim();
            query = query.Where(log => log.Action == action);
        }
        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = filter.Keyword.Trim();
            query = query.Where(log => log.Target.Contains(keyword) || log.Actor.Contains(keyword));
        }

        return await query
            .OrderByDescending(log => log.OccurredAtUtc).ThenByDescending(log => log.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetActorsAsync(CancellationToken cancellationToken = default) =>
        await dbContext.AuditLogs.AsNoTracking()
            .Select(log => log.Actor).Distinct().OrderBy(actor => actor)
            .ToListAsync(cancellationToken);

    /// <summary>Mốc 00:00 giờ địa phương của ngày, quy đổi sang UTC (giờ hiển thị trên màn hình nhật ký là giờ địa phương).</summary>
    public static DateTime StartOfLocalDayUtc(DateOnly date)
    {
        var localMidnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(localMidnight)) localMidnight = localMidnight.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(localMidnight, TimeZoneInfo.Local);
    }

    public async Task<AdminAccount?> GetSignedInStaffAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.Cookies.TryGetValue("admin_refresh", out var token) || string.IsNullOrWhiteSpace(token))
            return null;

        var hash = TokenService.HashRefreshToken(token);
        var now = DateTime.UtcNow;
        return await dbContext.RefreshTokens.AsNoTracking()
            .Where(item => item.TokenHash == hash && item.RevokedAtUtc == null && item.ExpiresAtUtc > now && item.AdminAccount.IsActive)
            .Select(item => item.AdminAccount)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Tạo bản ghi nhật ký (chưa lưu) để có thể lưu chung transaction với thao tác nghiệp vụ.</summary>
    public static AuditLog Create(string actor, string action, string target, string? ipAddress) => new()
    {
        OccurredAtUtc = DateTime.UtcNow,
        Actor = Truncate(actor, 256),
        Action = action,
        Target = Truncate(target, 500),
        IpAddress = NormalizeIp(ipAddress)
    };

    public static string NormalizeIp(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress)) return UnknownIpAddress;
        return IPAddress.TryParse(ipAddress, out var parsed) && parsed.IsIPv4MappedToIPv6
            ? parsed.MapToIPv4().ToString()
            : Truncate(ipAddress.Trim(), 45);
    }

    /// <summary>Chỉ Quản trị hệ thống (tài khoản còn hoạt động) được xem và tra cứu nhật ký.</summary>
    public static bool CanViewLogs(AdminAccount? account) =>
        account is { IsActive: true, Role: AccountRoles.SystemAdmin };

    public static string ClientIp(HttpContext context) => NormalizeIp(context.Connection.RemoteIpAddress?.ToString());

    public static string DescribeRole(string role) => role switch
    {
        AccountRoles.SystemAdmin => "quản trị hệ thống",
        AccountRoles.LibraryManager => "quản lý thư viện",
        AccountRoles.Librarian => "thủ thư",
        _ => role
    };

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
