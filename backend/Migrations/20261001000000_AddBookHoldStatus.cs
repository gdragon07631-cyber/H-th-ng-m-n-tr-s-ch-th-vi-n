using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261001000000_AddBookHoldStatus")]
public partial class AddBookHoldStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "Status", table: "BookHolds", type: "nvarchar(50)", maxLength: 50,
            nullable: false, defaultValue: "Đang chờ");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "Status", table: "BookHolds");
}
