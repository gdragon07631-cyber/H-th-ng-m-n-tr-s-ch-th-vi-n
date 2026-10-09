using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261009090000_AddBookLoanBookCopy")]
public sealed class AddBookLoanBookCopy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "BookCopyId", table: "BookLoans", type: "bigint", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_BookLoans_BookCopyId", table: "BookLoans", column: "BookCopyId", unique: true, filter: "[BookCopyId] IS NOT NULL");
        migrationBuilder.AddForeignKey(name: "FK_BookLoans_BookCopies_BookCopyId", table: "BookLoans", column: "BookCopyId", principalTable: "BookCopies", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_BookLoans_BookCopies_BookCopyId", table: "BookLoans");
        migrationBuilder.DropIndex(name: "IX_BookLoans_BookCopyId", table: "BookLoans");
        migrationBuilder.DropColumn(name: "BookCopyId", table: "BookLoans");
    }
}
