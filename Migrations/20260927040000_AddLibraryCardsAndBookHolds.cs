using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260927040000_AddLibraryCardsAndBookHolds")]
public partial class AddLibraryCardsAndBookHolds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "UpdatedAtUtc", table: "ReaderAccounts", type: "datetime2", nullable: false,
            defaultValueSql: "SYSUTCDATETIME()");

        migrationBuilder.CreateTable(
            name: "LibraryCardTypes",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
            },
            constraints: table => table.PrimaryKey("PK_LibraryCardTypes", x => x.Id));

        migrationBuilder.CreateTable(
            name: "BookHolds",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                ReaderAccountId = table.Column<int>(type: "int", nullable: false),
                BookId = table.Column<int>(type: "int", nullable: false),
                HeldAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BookHolds", x => x.Id);
                table.ForeignKey("FK_BookHolds_Books_BookId", x => x.BookId, "Books", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_BookHolds_ReaderAccounts_ReaderAccountId", x => x.ReaderAccountId, "ReaderAccounts", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "LibraryCards",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                CardCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                ReaderAccountId = table.Column<int>(type: "int", nullable: false),
                LibraryCardTypeId = table.Column<int>(type: "int", nullable: false),
                IssuedOn = table.Column<DateOnly>(type: "date", nullable: false),
                ExpiresOn = table.Column<DateOnly>(type: "date", nullable: false),
                Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Đang hoạt động")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LibraryCards", x => x.Id);
                table.ForeignKey("FK_LibraryCards_LibraryCardTypes_LibraryCardTypeId", x => x.LibraryCardTypeId, "LibraryCardTypes", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_LibraryCards_ReaderAccounts_ReaderAccountId", x => x.ReaderAccountId, "ReaderAccounts", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.InsertData("LibraryCardTypes", new[] { "Id", "IsActive", "Name" }, new object[,]
        {
            { 1, true, "Thẻ bạn đọc thường" }, { 2, true, "Thẻ sinh viên" },
            { 3, true, "Thẻ giảng viên" }, { 4, true, "Thẻ cán bộ" }
        });
        migrationBuilder.CreateIndex("IX_BookHolds_BookId", "BookHolds", "BookId");
        migrationBuilder.CreateIndex("IX_BookHolds_ReaderAccountId_BookId", "BookHolds", new[] { "ReaderAccountId", "BookId" }, unique: true);
        migrationBuilder.CreateIndex("IX_LibraryCardTypes_Name", "LibraryCardTypes", "Name", unique: true);
        migrationBuilder.CreateIndex("IX_LibraryCards_CardCode", "LibraryCards", "CardCode", unique: true);
        migrationBuilder.CreateIndex("IX_LibraryCards_LibraryCardTypeId", "LibraryCards", "LibraryCardTypeId");
        migrationBuilder.CreateIndex("IX_LibraryCards_ReaderAccountId", "LibraryCards", "ReaderAccountId", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("BookHolds");
        migrationBuilder.DropTable("LibraryCards");
        migrationBuilder.DropTable("LibraryCardTypes");
        migrationBuilder.DropColumn("UpdatedAtUtc", "ReaderAccounts");
    }
}
