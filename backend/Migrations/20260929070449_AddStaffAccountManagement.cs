using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Project.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffAccountManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "AdminAccounts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "AdminAccounts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StaffPasswordSetupTokens",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AdminAccountId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffPasswordSetupTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffPasswordSetupTokens_AdminAccounts_AdminAccountId",
                        column: x => x.AdminAccountId,
                        principalTable: "AdminAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AdminAccounts_Role_Valid",
                table: "AdminAccounts",
                sql: "[Role] IN ('Librarian', 'LibraryManager', 'SystemAdmin')");

            migrationBuilder.CreateIndex(
                name: "IX_StaffPasswordSetupTokens_AdminAccountId_ExpiresAtUtc",
                table: "StaffPasswordSetupTokens",
                columns: new[] { "AdminAccountId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffPasswordSetupTokens_TokenHash",
                table: "StaffPasswordSetupTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffPasswordSetupTokens");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AdminAccounts_Role_Valid",
                table: "AdminAccounts");

            migrationBuilder.DropColumn(
                name: "FullName",
                table: "AdminAccounts");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "AdminAccounts");
        }
    }
}
