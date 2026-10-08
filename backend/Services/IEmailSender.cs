namespace Project.Services;

public interface IEmailSender
{
    Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
