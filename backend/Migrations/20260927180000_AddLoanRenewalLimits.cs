using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260927180000_AddLoanRenewalLimits")]
public partial class AddLoanRenewalLimits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "RenewalCount", table: "BookLoans", type: "int", nullable: false, defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "MaxRenewals", table: "LibraryCardTypes", type: "int", nullable: false, defaultValue: 3);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "RenewalCount", table: "BookLoans");
        migrationBuilder.DropColumn(name: "MaxRenewals", table: "LibraryCardTypes");
    }
}
