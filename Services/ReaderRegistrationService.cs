using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Services;

public sealed class ReaderRegistrationService(
    ApplicationDbContext dbContext,
    IPasswordHasher<ReaderAccount> passwordHasher,
    ILogger<ReaderRegistrationService> logger) : IReaderRegistrationService
{
    public async Task<ReaderRegistrationOutcome> RegisterAsync(
        ReaderRegistrationViewModel model,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = model.Email.Trim().ToUpperInvariant();
        var normalizedCode = model.StudentOrStaffCode.Trim().ToUpperInvariant();

        // 1. Kiểm tra email đã tồn tại trước khi tạo tài khoản
        var emailExists = await dbContext.ReaderAccounts
            .AnyAsync(r => r.Email.ToUpper() == normalizedEmail, cancellationToken);

        // 2. Kiểm tra mã sinh viên hoặc mã cán bộ đã tồn tại trước khi tạo tài khoản
        var codeExists = await dbContext.ReaderAccounts
            .AnyAsync(r => r.StudentOrStaffCode.ToUpper() == normalizedCode, cancellationToken);

        // 8. Tài khoản bị từ chối do trùng email hoặc trùng mã KHÔNG được tạo thêm bản ghi mới
        if (emailExists || codeExists)
        {
            if (emailExists)
            {
                logger.LogWarning("Đăng ký bị từ chối: Email {Email} đã tồn tại trong hệ thống.", model.Email);
            }
            if (codeExists)
            {
                logger.LogWarning("Đăng ký bị từ chối: Mã {Code} đã tồn tại trong hệ thống.", model.StudentOrStaffCode);
            }

            return ReaderRegistrationOutcome.Duplicate(emailExists, codeExists);
        }

        // 6 & 7. Cho phép đăng ký bình thường với trạng thái "Chờ duyệt"
        var readerAccount = new ReaderAccount
        {
            FullName = model.FullName.Trim(),
            DateOfBirth = model.DateOfBirth!.Value,
            Email = model.Email.Trim(),
            PhoneNumber = model.PhoneNumber.Trim(),
            StudentOrStaffCode = model.StudentOrStaffCode.Trim(),
            Status = "Chờ duyệt",
            CreatedAtUtc = DateTime.UtcNow
        };

        readerAccount.PasswordHash = passwordHasher.HashPassword(readerAccount, model.Password);

        dbContext.ReaderAccounts.Add(readerAccount);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Đăng ký thành công tài khoản Bạn đọc: {Email}, Mã: {Code}, Trạng thái: Chờ duyệt.",
            readerAccount.Email, readerAccount.StudentOrStaffCode);

        return ReaderRegistrationOutcome.Success(readerAccount);
    }

    public async Task<ReaderAccount?> GetReaderByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await dbContext.ReaderAccounts.FindAsync([id], cancellationToken);
    }

    public async Task<ReaderAccount?> AuthenticateReaderAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var reader = await dbContext.ReaderAccounts
            .SingleOrDefaultAsync(r => r.Email.ToUpper() == normalizedEmail, cancellationToken);

        if (reader == null)
        {
            return null;
        }

        var verificationResult = passwordHasher.VerifyHashedPassword(reader, reader.PasswordHash, password);
        return verificationResult != PasswordVerificationResult.Failed ? reader : null;
    }

    public async Task<DocumentHoldOutcome> HoldDocumentAsync(
        int readerAccountId,
        int documentId,
        CancellationToken cancellationToken = default)
    {
        var reader = await dbContext.ReaderAccounts.FindAsync([readerAccountId], cancellationToken);
        if (reader == null)
        {
            return DocumentHoldOutcome.Failed("Không tìm thấy thông tin tài khoản Bạn đọc.");
        }

        // 4 & 5. Kiểm tra trạng thái hiện tại của tài khoản. Nếu đang "Chờ duyệt", từ chối thao tác đặt giữ tài liệu.
        if (string.Equals(reader.Status, "Chờ duyệt", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Từ chối đặt giữ: Tài khoản Bạn đọc {Email} đang ở trạng thái Chờ duyệt.", reader.Email);

            // 6. Hiển thị thông báo rõ ràng rằng tài khoản cần được duyệt trước khi đặt giữ tài liệu
            return DocumentHoldOutcome.Rejected(
                "Tài khoản của bạn đang ở trạng thái Chờ duyệt. Vui lòng xuất trình giấy tờ tại quầy thư viện để được duyệt tài khoản trước khi thực hiện đặt giữ tài liệu.");
        }

        // 7. Nếu tài khoản đã được duyệt, cho phép thực hiện chức năng đặt giữ
        logger.LogInformation("Đặt giữ thành công tài liệu #{DocumentId} cho tài khoản {Email} (Trạng thái: {Status}).",
            documentId, reader.Email, reader.Status);

        return DocumentHoldOutcome.Success($"Đặt giữ thành công tài liệu #{documentId}.");
    }
}
