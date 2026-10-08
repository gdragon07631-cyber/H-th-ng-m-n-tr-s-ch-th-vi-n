using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260927190000_AddReaderPasswordHistories")]
public partial class AddReaderPasswordHistories : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ReaderPasswordHistories",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ReaderAccountId = table.Column<int>(type: "int", nullable: false),
                PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReaderPasswordHistories", x => x.Id);
                table.ForeignKey(
                    name: "FK_ReaderPasswordHistories_ReaderAccounts_ReaderAccountId",
                    column: x => x.ReaderAccountId,
                    principalTable: "ReaderAccounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ReaderPasswordHistories_ReaderAccountId_CreatedAtUtc",
            table: "ReaderPasswordHistories",
            columns: new[] { "ReaderAccountId", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ReaderPasswordHistories");
    }
}
