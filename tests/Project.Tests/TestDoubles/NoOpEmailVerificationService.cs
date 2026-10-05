using Project.Models;
using Project.Services;

namespace Project.Tests;

/// <summary>Dùng cho các test không liên quan tới xác nhận email: không gửi gì, không đổi dữ liệu.</summary>
internal sealed class NoOpEmailVerificationService : IReaderEmailVerificationService
{
    public Task<EmailVerificationSendResult> SendAsync(ReaderAccount reader, string confirmUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult(new EmailVerificationSendResult(true, null));

    public Task<EmailVerificationSendResult> SendEmailChangeAsync(ReaderAccount reader, string confirmUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult(new EmailVerificationSendResult(true, null));

    public Task<EmailVerificationSendResult?> ResendAsync(string email, string confirmUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult<EmailVerificationSendResult?>(null);

    public Task<EmailConfirmationOutcome> ConfirmAsync(string token, CancellationToken cancellationToken = default) =>
        Task.FromResult(new EmailConfirmationOutcome(EmailConfirmationResult.InvalidOrExpired));

    public Task<EmailConfirmationOutcome> ConfirmByAdminAsync(int readerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new EmailConfirmationOutcome(EmailConfirmationResult.InvalidOrExpired));
}
