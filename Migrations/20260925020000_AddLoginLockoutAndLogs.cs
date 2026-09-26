using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Project.Data;

#nullable disable

namespace Project.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260925020000_AddLoginLockoutAndLogs")]
public partial class AddLoginLockoutAndLogs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "FailedLoginAttempts", table: "AdminAccounts", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<DateTime>(
            name: "LockoutEndUtc", table: "AdminAccounts", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<DateTime>(
            name: "LockoutStartUtc", table: "AdminAccounts", type: "datetime2", nullable: true);
        migrationBuilder.AddCheckConstraint(
            name: "CK_AdminAccounts_FailedLoginAttempts_NonNegative",
            table: "AdminAccounts", sql: "[FailedLoginAttempts] >= 0");

        migrationBuilder.CreateTable(
            name: "LoginLogs",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                AttemptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                AdminAccountId = table.Column<int>(type: "int", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LoginLogs", x => x.Id);
                table.ForeignKey(
                    name: "FK_LoginLogs_AdminAccounts_AdminAccountId",
                    column: x => x.AdminAccountId,
                    principalTable: "AdminAccounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex("IX_LoginLogs_AdminAccountId", "LoginLogs", "AdminAccountId");
        migrationBuilder.CreateIndex("IX_LoginLogs_AttemptedAtUtc", "LoginLogs", "AttemptedAtUtc");
        migrationBuilder.CreateIndex("IX_LoginLogs_Email", "LoginLogs", "Email");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "LoginLogs");
        migrationBuilder.DropCheckConstraint("CK_AdminAccounts_FailedLoginAttempts_NonNegative", "AdminAccounts");
        migrationBuilder.DropColumn("FailedLoginAttempts", "AdminAccounts");
        migrationBuilder.DropColumn("LockoutEndUtc", "AdminAccounts");
        migrationBuilder.DropColumn("LockoutStartUtc", "AdminAccounts");
    }
}
