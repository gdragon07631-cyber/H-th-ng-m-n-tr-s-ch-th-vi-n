using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261003010000_AddBookAuthors")]
public sealed class AddBookAuthors : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BookAuthors",
            columns: table => new
            {
                BookId = table.Column<int>(type: "int", nullable: false),
                AuthorId = table.Column<int>(type: "int", nullable: false),
                SortOrder = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BookAuthors", x => new { x.BookId, x.AuthorId });
                table.ForeignKey(
                    name: "FK_BookAuthors_Authors_AuthorId",
                    column: x => x.AuthorId,
                    principalTable: "Authors",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BookAuthors_Books_BookId",
                    column: x => x.BookId,
                    principalTable: "Books",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BookAuthors_AuthorId",
            table: "BookAuthors",
            column: "AuthorId");

        // Đầu sách đã biên mục trước Lát 2 giữ nguyên tác giả hiện có làm tác giả đầu tiên.
        migrationBuilder.Sql("INSERT INTO [BookAuthors] ([BookId], [AuthorId], [SortOrder]) SELECT [Id], [AuthorId], 0 FROM [Books];");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "BookAuthors");
    }
}
