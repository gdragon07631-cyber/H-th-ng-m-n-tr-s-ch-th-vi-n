using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261001010000_AddBookHoldPickupDeadline")]
public partial class AddBookHoldPickupDeadline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<DateTime>(
            name: "PickupDeadlineUtc", table: "BookHolds", type: "datetime2", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "PickupDeadlineUtc", table: "BookHolds");
}
