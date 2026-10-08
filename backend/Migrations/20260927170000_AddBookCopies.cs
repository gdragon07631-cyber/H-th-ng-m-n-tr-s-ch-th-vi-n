using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260927170000_AddBookCopies")]
    public partial class AddBookCopies : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookCopies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    ShelfId = table.Column<int>(type: "int", nullable: false),
                    CopyCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Sẵn sàng")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookCopies", x => x.Id);
                    table.ForeignKey("FK_BookCopies_Books_BookId", x => x.BookId, "Books", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_BookCopies_Shelves_ShelfId", x => x.ShelfId, "Shelves", "Id", onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex(name: "IX_BookCopies_BookId", table: "BookCopies", column: "BookId");
            migrationBuilder.CreateIndex(name: "IX_BookCopies_CopyCode", table: "BookCopies", column: "CopyCode", unique: true);
            migrationBuilder.CreateIndex(name: "IX_BookCopies_ShelfId", table: "BookCopies", column: "ShelfId");
        }

        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "BookCopies");
    }
}
