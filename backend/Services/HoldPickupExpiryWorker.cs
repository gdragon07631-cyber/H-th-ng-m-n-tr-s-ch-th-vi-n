namespace Project.Services;

/// <summary>Định kỳ tự hủy các đơn đặt giữ quá hạn nhận sách (chạy ngay khi khởi động, sau đó mỗi 5 phút).</summary>
public sealed class HoldPickupExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<HoldPickupExpiryWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var expiry = scope.ServiceProvider.GetRequiredService<IHoldPickupExpiryService>();
                await expiry.ExpireOverdueAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Lỗi tạm thời (ví dụ database chưa sẵn sàng) không được làm dừng ứng dụng; thử lại ở lượt sau.
                logger.LogError(exception, "Lượt tự hủy đơn đặt giữ quá hạn nhận bị lỗi.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
