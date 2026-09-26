namespace Project.Services;

public interface ITokenService
{
    Task<TokenPair> CreateTokenPairAsync(int adminAccountId, CancellationToken cancellationToken = default);

    Task<TokenPair?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
}
