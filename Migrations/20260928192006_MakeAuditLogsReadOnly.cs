using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Project.Migrations
{
    /// <summary>Nhật ký hoạt động chỉ được thêm mới: mọi UPDATE/DELETE trên AuditLogs đều bị database từ chối.</summary>
    public partial class MakeAuditLogsReadOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_AuditLogs_ReadOnly] ON [AuditLogs]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, N'Nhật ký hoạt động là dữ liệu chỉ đọc, không được sửa hoặc xóa.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_AuditLogs_ReadOnly];");
        }
    }
}
