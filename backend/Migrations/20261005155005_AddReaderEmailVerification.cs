using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Project.Migrations
{
    /// <inheritdoc />
    public partial class AddReaderEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailConfirmed",
                table: "ReaderAccounts",
                type: "bit",
                nullable: false,
                defaultValue: true); // Tài khoản đã có trước chức năng này được coi là đã xác nhận email.

            migrationBuilder.CreateTable(
                name: "ReaderEmailVerificationTokens",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReaderAccountId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReaderEmailVerificationTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReaderEmailVerificationTokens_ReaderAccounts_ReaderAccountId",
                        column: x => x.ReaderAccountId,
                        principalTable: "ReaderAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReaderEmailVerificationTokens_ReaderAccountId_CreatedAtUtc",
                table: "ReaderEmailVerificationTokens",
                columns: new[] { "ReaderAccountId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReaderEmailVerificationTokens_TokenHash",
                table: "ReaderEmailVerificationTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReaderEmailVerificationTokens");

            migrationBuilder.DropColumn(
                name: "EmailConfirmed",
                table: "ReaderAccounts");
        }
    }
}
