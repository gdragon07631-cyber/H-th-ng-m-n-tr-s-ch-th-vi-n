using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Project.Migrations;

[Migration("20261009150000_AddLoanContactHistories")]
public partial class AddLoanContactHistories : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LoanContactHistories",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BookLoanId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                ContactedByAdminAccountId = table.Column<int>(type: "int", nullable: true),
                ContactedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LoanContactHistories", x => x.Id);
                table.ForeignKey(
                    name: "FK_LoanContactHistories_AdminAccounts_ContactedByAdminAccountId",
                    column: x => x.ContactedByAdminAccountId,
                    principalTable: "AdminAccounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_LoanContactHistories_BookLoans_BookLoanId",
                    column: x => x.BookLoanId,
                    principalTable: "BookLoans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LoanContactHistories_BookLoanId_CreatedAtUtc",
            table: "LoanContactHistories",
            columns: new[] { "BookLoanId", "CreatedAtUtc" });
        migrationBuilder.CreateIndex(
            name: "IX_LoanContactHistories_ContactedByAdminAccountId",
            table: "LoanContactHistories",
            column: "ContactedByAdminAccountId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "LoanContactHistories");
}
