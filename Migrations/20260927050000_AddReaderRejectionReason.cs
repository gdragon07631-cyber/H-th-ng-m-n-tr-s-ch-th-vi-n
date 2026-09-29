using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260927050000_AddReaderRejectionReason")]
public partial class AddReaderRejectionReason : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "RejectionReason",
            table: "ReaderAccounts",
            type: "nvarchar(1000)",
            maxLength: 1000,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "RejectionReason", table: "ReaderAccounts");
    }
}
