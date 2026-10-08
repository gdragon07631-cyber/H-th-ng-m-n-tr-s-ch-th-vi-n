using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260927050000_AddReaderPasswordResetRequests")]
public partial class AddReaderPasswordResetRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ReaderPasswordResetRequests",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                EmailHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ReaderPasswordResetRequests", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_ReaderPasswordResetRequests_EmailHash_RequestedAtUtc",
            table: "ReaderPasswordResetRequests",
            columns: new[] { "EmailHash", "RequestedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "ReaderPasswordResetRequests");
}
