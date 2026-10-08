using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261004000000_AddBookHoldCancellationDetails")]
public partial class AddBookHoldCancellationDetails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "CancellationReason", table: "BookHolds", type: "nvarchar(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "CancelledAtUtc", table: "BookHolds", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<int>(name: "CancelledByAdminAccountId", table: "BookHolds", type: "int", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CancellationReason", table: "BookHolds");
        migrationBuilder.DropColumn(name: "CancelledAtUtc", table: "BookHolds");
        migrationBuilder.DropColumn(name: "CancelledByAdminAccountId", table: "BookHolds");
    }
}
