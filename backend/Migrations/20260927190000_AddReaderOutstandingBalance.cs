using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260927190000_AddReaderOutstandingBalance")]
public partial class AddReaderOutstandingBalance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<decimal>(
            name: "OutstandingBalance", table: "ReaderAccounts", type: "decimal(18,2)",
            nullable: false, defaultValue: 0m);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "OutstandingBalance", table: "ReaderAccounts");
}
