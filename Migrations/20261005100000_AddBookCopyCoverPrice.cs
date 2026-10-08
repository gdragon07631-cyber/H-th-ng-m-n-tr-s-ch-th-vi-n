using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable
namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261005100000_AddBookCopyCoverPrice")]
public sealed class AddBookCopyCoverPrice : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<decimal>(name: "CoverPrice", table: "BookCopies", type: "decimal(18,2)", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "CoverPrice", table: "BookCopies");
}
