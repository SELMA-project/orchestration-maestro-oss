using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace orchestrationdb.Migrations
{
    public partial class JobMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                table: "Jobs",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Metadata",
                table: "Jobs");
        }
    }
}
