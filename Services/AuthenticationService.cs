using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Data;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class AuthenticationService(
    ApplicationDbContext dbContext,
    IPasswordHasher<AdminAccount> passwordHasher,
    ILogger<AuthenticationService> logger) : IAuthenticationService
{
    public async Task<LoginOutcome> LoginAsync(
        string email,
        string password,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var normalizedEmail = email.Trim().ToUpperInvariant();
            var account = await dbContext.AdminAccounts
                .SingleOrDefaultAsync(item => item.Email.ToUpper() == normalizedEmail, cancellationToken);
            var now = DateTime.UtcNow;
            var status = "LoginFailed";
            LoginResult result;

            if (account?.LockoutEndUtc is { } lockoutEnd && lockoutEnd > now)
            {
                status = "AccountLocked";
                result = LoginResult.AccountLocked;
            }
            else
            {
                if (account is not null && account.LockoutEndUtc.HasValue && account.LockoutEndUtc <= now)
                {
                    account.FailedLoginAttempts = 0;
                    account.LockoutStartUtc = null;
                    account.LockoutEndUtc = null;
                }

                var verified = account is not null && account.IsActive &&
                               passwordHasher.VerifyHashedPassword(account, account.PasswordHash, password) !=
                               PasswordVerificationResult.Failed;

                if (verified)
                {
                    account!.FailedLoginAttempts = 0;
                    account.LockoutStartUtc = null;
                    account.LockoutEndUtc = null;
                    status = "LoginSuccess";
                    result = LoginResult.LoginSuccess;
                }
                else
                {
                    if (account is not null && account.IsActive)
                    {
                        account.FailedLoginAttempts++;
                        if (account.FailedLoginAttempts >= 5)
                        {
                            account.LockoutStartUtc = now;
                            account.LockoutEndUtc = now.AddMinutes(15);
                            status = "AccountLocked";
                            result = LoginResult.AccountLocked;
                        }
                        else
                        {
                            result = LoginResult.LoginFailed;
                        }
                    }
                    else
                    {
                        result = LoginResult.LoginFailed;
                    }
                }
            }

            dbContext.LoginLogs.Add(new LoginLog
            {
                Email = email.Trim(),
                IpAddress = ipAddress,
                AttemptedAtUtc = now,
                Status = status,
                AdminAccountId = account?.Id
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("{LoginStatus}", status);
            return new LoginOutcome(result, result == LoginResult.LoginSuccess ? account?.Id : null);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
