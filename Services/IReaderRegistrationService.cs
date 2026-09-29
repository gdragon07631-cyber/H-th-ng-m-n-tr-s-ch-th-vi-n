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

public enum ReaderContactUpdateResult
{
    Success,
    NotFound,
    InvalidCurrentPassword
}

public enum ReaderPasswordChangeResult
{
    Success,
    NotFound,
    IncorrectCurrentPassword,
    PasswordRecentlyUsed
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

public sealed class ReaderApprovalOutcome
{
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }
    public LibraryCard? LibraryCard { get; }

    private ReaderApprovalOutcome(bool isSuccess, string? errorMessage, LibraryCard? libraryCard)
        => (IsSuccess, ErrorMessage, LibraryCard) = (isSuccess, errorMessage, libraryCard);

    public static ReaderApprovalOutcome Success(LibraryCard card) => new(true, null, card);
    public static ReaderApprovalOutcome Failed(string message) => new(false, message, null);
}

public sealed class ReaderRejectionOutcome
{
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }

    private ReaderRejectionOutcome(bool isSuccess, string? errorMessage) => (IsSuccess, ErrorMessage) = (isSuccess, errorMessage);

    public static ReaderRejectionOutcome Success() => new(true, null);
    public static ReaderRejectionOutcome Failed(string message) => new(false, message);
}

public interface IReaderRegistrationService
{
    Task<ReaderRegistrationOutcome> RegisterAsync(ReaderRegistrationViewModel model, CancellationToken cancellationToken = default);
    Task<ReaderAccount?> GetReaderByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ReaderContactUpdateResult> UpdateReaderContactAsync(
        int id, string phoneNumber, string address, string email, string currentPassword,
        CancellationToken cancellationToken = default);
    Task<ReaderPasswordChangeResult> ChangeReaderPasswordAsync(
        int id, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
    Task<ReaderAccount?> AuthenticateReaderAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<DocumentHoldOutcome> HoldDocumentAsync(int readerAccountId, int documentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReaderAccount>> GetPendingReadersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReaderAccount>> GetPendingReadersAsync(string? search, DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LibraryCardType>> GetActiveCardTypesAsync(CancellationToken cancellationToken = default);
    Task<ReaderApprovalOutcome> ApproveReaderAsync(ApproveReaderViewModel model, CancellationToken cancellationToken = default);
    Task<ReaderRejectionOutcome> RejectReaderAsync(int readerAccountId, string? rejectionReason, CancellationToken cancellationToken = default);
}
