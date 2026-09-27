using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260927020000_AddCategories")]
public partial class AddCategories : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Categories",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Hoạt động"),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Categories", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_Categories_Name", table: "Categories", column: "Name", unique: true);
        migrationBuilder.AddColumn<int>(name: "CategoryId", table: "Books", type: "int", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_Books_CategoryId", table: "Books", column: "CategoryId");
        migrationBuilder.AddForeignKey(
            name: "FK_Books_Categories_CategoryId",
            table: "Books",
            column: "CategoryId",
            principalTable: "Categories",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_Books_Categories_CategoryId", table: "Books");
        migrationBuilder.DropIndex(name: "IX_Books_CategoryId", table: "Books");
        migrationBuilder.DropColumn(name: "CategoryId", table: "Books");
        migrationBuilder.DropTable(name: "Categories");
    }
}
