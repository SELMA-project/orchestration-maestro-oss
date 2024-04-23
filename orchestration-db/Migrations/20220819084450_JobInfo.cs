using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace orchestrationdb.Migrations
{
    public partial class JobInfo : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Jobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Scenario",
                table: "Jobs",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "Scenario",
                table: "Jobs");
        }
    }
}
