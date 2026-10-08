using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261003020000_AddBookCopyStatusReason")]
public sealed class AddBookCopyStatusReason : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "StatusReason",
            table: "BookCopies",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "StatusReason", table: "BookCopies");
}
