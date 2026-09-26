namespace Project.Services;

public sealed record LoginOutcome(LoginResult Result, int? AdminAccountId = null);
