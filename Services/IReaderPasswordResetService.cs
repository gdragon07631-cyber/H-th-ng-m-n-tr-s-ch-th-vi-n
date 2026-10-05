namespace Project.Services;

public interface IReaderPasswordResetService
{
    Task<bool> RequestAsync(string email, string resetUrl, CancellationToken cancellationToken = default);
    Task<bool> IsTokenValidAsync(string token, CancellationToken cancellationToken = default);
    Task<bool> ResetAsync(string token, string newPassword, CancellationToken cancellationToken = default);
}
