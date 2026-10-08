using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261003000000_AddBookBasicCatalogFields")]
public sealed class AddBookBasicCatalogFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Subtitle", table: "Books", type: "nvarchar(250)", maxLength: 250, nullable: true);
        migrationBuilder.AddColumn<string>(name: "Publisher", table: "Books", type: "nvarchar(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<int>(name: "PublicationYear", table: "Books", type: "int", nullable: true);
        migrationBuilder.AddColumn<int>(name: "PageCount", table: "Books", type: "int", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Subtitle", table: "Books");
        migrationBuilder.DropColumn(name: "Publisher", table: "Books");
        migrationBuilder.DropColumn(name: "PublicationYear", table: "Books");
        migrationBuilder.DropColumn(name: "PageCount", table: "Books");
    }
}
