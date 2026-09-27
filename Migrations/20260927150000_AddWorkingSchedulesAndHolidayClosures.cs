using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Project.Data;

#nullable disable

namespace Project.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260927150000_AddWorkingSchedulesAndHolidayClosures")]
    public partial class AddWorkingSchedulesAndHolidayClosures : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HolidayClosures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HolidayDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_HolidayClosures", x => x.Id));

            migrationBuilder.CreateTable(
                name: "WeeklyWorkingSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_WeeklyWorkingSchedules", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_HolidayClosures_HolidayDate",
                table: "HolidayClosures",
                column: "HolidayDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyWorkingSchedules_DayOfWeek",
                table: "WeeklyWorkingSchedules",
                column: "DayOfWeek",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "HolidayClosures");
            migrationBuilder.DropTable(name: "WeeklyWorkingSchedules");
        }
    }
}
