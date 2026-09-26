using Project.Models;

namespace Project.Services;

public sealed class ReaderRegistrationOutcome
{
    public bool IsSuccess { get; }
    public bool IsEmailDuplicate { get; }
    public bool IsCodeDuplicate { get; }
    public ReaderAccount? Account { get; }

    public ReaderRegistrationOutcome(bool isSuccess, bool isEmailDuplicate, bool isCodeDuplicate, ReaderAccount? account = null)
    {
        IsSuccess = isSuccess;
        IsEmailDuplicate = isEmailDuplicate;
        IsCodeDuplicate = isCodeDuplicate;
        Account = account;
    }

    public static ReaderRegistrationOutcome Success(ReaderAccount account) =>
        new(true, false, false, account);

    public static ReaderRegistrationOutcome Duplicate(bool emailDuplicate, bool codeDuplicate) =>
        new(false, emailDuplicate, codeDuplicate, null);
}

public sealed class DocumentHoldOutcome
{
    public bool IsAllowed { get; }
    public string Message { get; }

    private DocumentHoldOutcome(bool isAllowed, string message)
    {
        IsAllowed = isAllowed;
        Message = message;
    }

    public static DocumentHoldOutcome Success(string message) => new(true, message);
    public static DocumentHoldOutcome Rejected(string message) => new(false, message);
    public static DocumentHoldOutcome Failed(string message) => new(false, message);
}

public interface IReaderRegistrationService
{
    Task<ReaderRegistrationOutcome> RegisterAsync(ReaderRegistrationViewModel model, CancellationToken cancellationToken = default);
    Task<ReaderAccount?> GetReaderByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ReaderAccount?> AuthenticateReaderAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<DocumentHoldOutcome> HoldDocumentAsync(int readerAccountId, int documentId, CancellationToken cancellationToken = default);
}
