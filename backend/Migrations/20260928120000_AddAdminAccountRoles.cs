using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260928120000_AddAdminAccountRoles")]
public partial class AddAdminAccountRoles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Role",
            table: "AdminAccounts",
            type: "nvarchar(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: "SystemAdmin");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Role", table: "AdminAccounts");
    }
}
