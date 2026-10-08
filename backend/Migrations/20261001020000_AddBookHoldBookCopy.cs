using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261001020000_AddBookHoldBookCopy")]
public partial class AddBookHoldBookCopy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "BookCopyId", table: "BookHolds", type: "bigint", nullable: true);
        migrationBuilder.CreateIndex("IX_BookHolds_BookCopyId", "BookHolds", "BookCopyId");
        migrationBuilder.AddForeignKey(
            name: "FK_BookHolds_BookCopies_BookCopyId", table: "BookHolds", column: "BookCopyId",
            principalTable: "BookCopies", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_BookHolds_BookCopies_BookCopyId", table: "BookHolds");
        migrationBuilder.DropIndex(name: "IX_BookHolds_BookCopyId", table: "BookHolds");
        migrationBuilder.DropColumn(name: "BookCopyId", table: "BookHolds");
    }
}
