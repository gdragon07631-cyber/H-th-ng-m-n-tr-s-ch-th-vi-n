namespace Project.Services;

public interface ITokenService
{
    Task<TokenPair> CreateTokenPairAsync(int adminAccountId, CancellationToken cancellationToken = default);

    Task<TokenPair?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Thu hồi phiên đăng nhập (đăng xuất). Trả về vai trò của chủ phiên, hoặc null nếu không tìm thấy.</summary>
    Task<string?> RevokeAsync(string refreshToken, CancellationToken cancellationToken = default);
}
