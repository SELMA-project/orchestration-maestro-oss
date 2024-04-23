using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace orchestrationdb.Migrations
{
    public partial class JobUpdatedIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Updated",
                table: "Jobs",
                column: "Updated");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Jobs_Updated",
                table: "Jobs");
        }
    }
}
