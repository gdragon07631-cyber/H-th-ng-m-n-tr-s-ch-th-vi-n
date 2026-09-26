using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class TokenService(ApplicationDbContext dbContext, IConfiguration configuration) : ITokenService
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    public async Task<TokenPair> CreateTokenPairAsync(int adminAccountId, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.AdminAccounts
            .SingleAsync(item => item.Id == adminAccountId && item.IsActive, cancellationToken);
        var now = DateTime.UtcNow;
        var accessToken = CreateAccessToken(account, now);
        var refreshValue = CreateRefreshTokenValue();
        var refreshExpiresAt = now.Add(RefreshTokenLifetime);

        dbContext.RefreshTokens.Add(new RefreshToken
        {
            AdminAccountId = account.Id,
            TokenHash = HashRefreshToken(refreshValue),
            CreatedAtUtc = now,
            ExpiresAtUtc = refreshExpiresAt
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return new TokenPair(accessToken, now.Add(AccessTokenLifetime), refreshValue, refreshExpiresAt);
    }

    public async Task<TokenPair?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            var oldHash = HashRefreshToken(refreshToken);
            var storedToken = await dbContext.RefreshTokens
                .Include(item => item.AdminAccount)
                .SingleOrDefaultAsync(item => item.TokenHash == oldHash, cancellationToken);

            if (storedToken is null || storedToken.RevokedAtUtc.HasValue || storedToken.ExpiresAtUtc <= now ||
                !storedToken.AdminAccount.IsActive)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            var newRefreshValue = CreateRefreshTokenValue();
            var newRefreshHash = HashRefreshToken(newRefreshValue);
            var refreshExpiresAt = now.Add(RefreshTokenLifetime);
            var accessToken = CreateAccessToken(storedToken.AdminAccount, now);

            storedToken.RevokedAtUtc = now;
            storedToken.ReplacedByTokenHash = newRefreshHash;
            dbContext.RefreshTokens.Add(new RefreshToken
            {
                AdminAccountId = storedToken.AdminAccountId,
                TokenHash = newRefreshHash,
                CreatedAtUtc = now,
                ExpiresAtUtc = refreshExpiresAt
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new TokenPair(
                accessToken,
                now.Add(AccessTokenLifetime),
                newRefreshValue,
                refreshExpiresAt);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public static string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    private string CreateAccessToken(AdminAccount account, DateTime issuedAtUtc)
    {
        var keyText = configuration["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(keyText) || Encoding.UTF8.GetByteCount(keyText) < 32)
        {
            throw new InvalidOperationException(
                "JWT signing key is missing or too short. Configure Jwt__SigningKey with at least 32 bytes via a secure environment variable or secret store.");
        }

        var issuer = configuration["Jwt:Issuer"] ?? "Project";
        var audience = configuration["Jwt:Audience"] ?? "Project.Admin";
        var expiresAtUtc = issuedAtUtc.Add(AccessTokenLifetime);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyText));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, account.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, account.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        var token = new JwtSecurityToken(issuer, audience, claims, issuedAtUtc, expiresAtUtc, credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string CreateRefreshTokenValue()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
