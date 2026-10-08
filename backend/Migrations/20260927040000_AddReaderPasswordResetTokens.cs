using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260927040000_AddReaderPasswordResetTokens")]
public partial class AddReaderPasswordResetTokens : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ReaderPasswordResetTokens",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ReaderAccountId = table.Column<int>(type: "int", nullable: false),
                TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReaderPasswordResetTokens", x => x.Id);
                table.ForeignKey("FK_ReaderPasswordResetTokens_ReaderAccounts_ReaderAccountId",
                    x => x.ReaderAccountId, "ReaderAccounts", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_ReaderPasswordResetTokens_TokenHash", "ReaderPasswordResetTokens", "TokenHash", unique: true);
        migrationBuilder.CreateIndex("IX_ReaderPasswordResetTokens_ReaderAccountId_ExpiresAtUtc", "ReaderPasswordResetTokens", new[] { "ReaderAccountId", "ExpiresAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "ReaderPasswordResetTokens");
}
