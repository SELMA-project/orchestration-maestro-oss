using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

namespace orchestrationdb.Migrations
{
    public partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Updated = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "timezone('UTC', now())"),
                    Created = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "timezone('UTC', now())"),
                    Status = table.Column<string>(type: "text", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Dependencies = table.Column<HashSet<Guid>>(type: "jsonb", nullable: true),
                    OG_Dependencies = table.Column<HashSet<Guid>>(type: "jsonb", nullable: true),
                    Type = table.Column<string>(type: "text", nullable: true),
                    Provider = table.Column<string>(type: "text", nullable: true),
                    Request = table.Column<string>(type: "text", nullable: true),
                    Input = table.Column<string>(type: "text", nullable: true),
                    Result = table.Column<string>(type: "text", nullable: true),
                    Scripts = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemVariables",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemVariables", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Status",
                table: "Jobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_WorkflowId",
                table: "Jobs",
                column: "WorkflowId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Jobs");

            migrationBuilder.DropTable(
                name: "SystemVariables");
        }
    }
}
