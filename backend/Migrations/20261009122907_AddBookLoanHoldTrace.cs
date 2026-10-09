using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Project.Migrations
{
    /// <inheritdoc />
    public partial class AddBookLoanHoldTrace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CreatedByAdminAccountId",
                table: "BookLoans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SourceBookHoldId",
                table: "BookLoans",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookLoans_CreatedByAdminAccountId",
                table: "BookLoans",
                column: "CreatedByAdminAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BookLoans_SourceBookHoldId",
                table: "BookLoans",
                column: "SourceBookHoldId",
                unique: true,
                filter: "[SourceBookHoldId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_BookLoans_AdminAccounts_CreatedByAdminAccountId",
                table: "BookLoans",
                column: "CreatedByAdminAccountId",
                principalTable: "AdminAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BookLoans_BookHolds_SourceBookHoldId",
                table: "BookLoans",
                column: "SourceBookHoldId",
                principalTable: "BookHolds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BookLoans_AdminAccounts_CreatedByAdminAccountId",
                table: "BookLoans");

            migrationBuilder.DropForeignKey(
                name: "FK_BookLoans_BookHolds_SourceBookHoldId",
                table: "BookLoans");

            migrationBuilder.DropIndex(
                name: "IX_BookLoans_CreatedByAdminAccountId",
                table: "BookLoans");

            migrationBuilder.DropIndex(
                name: "IX_BookLoans_SourceBookHoldId",
                table: "BookLoans");

            migrationBuilder.DropColumn(
                name: "CreatedByAdminAccountId",
                table: "BookLoans");

            migrationBuilder.DropColumn(
                name: "SourceBookHoldId",
                table: "BookLoans");
        }
    }
}
