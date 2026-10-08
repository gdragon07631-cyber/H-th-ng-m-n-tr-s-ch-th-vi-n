using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261005090000_AddActiveBookHoldGuards")]
public partial class AddActiveBookHoldGuards : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_BookHolds_ReaderAccountId_BookId",
            table: "BookHolds");

        migrationBuilder.CreateIndex(
            name: "IX_BookHolds_ReaderAccountId_Status_BookId",
            table: "BookHolds",
            columns: new[] { "ReaderAccountId", "Status", "BookId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_BookHolds_ReaderAccountId_Status_BookId",
            table: "BookHolds");

        migrationBuilder.CreateIndex(
            name: "IX_BookHolds_ReaderAccountId_BookId",
            table: "BookHolds",
            columns: new[] { "ReaderAccountId", "BookId" },
            unique: true);
    }
}
