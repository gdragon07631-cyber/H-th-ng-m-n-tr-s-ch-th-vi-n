using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260927160000_AddBookLoans")]
    public partial class AddBookLoans : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookLoans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    ReaderAccountId = table.Column<int>(type: "int", nullable: false),
                    LoanDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OriginalDueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookLoans", x => x.Id);
                    table.ForeignKey("FK_BookLoans_Books_BookId", x => x.BookId, "Books", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_BookLoans_ReaderAccounts_ReaderAccountId", x => x.ReaderAccountId, "ReaderAccounts", "Id", onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex(name: "IX_BookLoans_BookId", table: "BookLoans", column: "BookId");
            migrationBuilder.CreateIndex(name: "IX_BookLoans_ReaderAccountId", table: "BookLoans", column: "ReaderAccountId");
        }

        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "BookLoans");
    }
}
