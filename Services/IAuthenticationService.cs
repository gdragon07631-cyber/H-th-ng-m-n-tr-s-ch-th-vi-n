using Project.Models;

namespace Project.Services;

public interface IAuthenticationService
{
    Task<LoginOutcome> LoginAsync(string email, string password, string? ipAddress, CancellationToken cancellationToken = default);
}
