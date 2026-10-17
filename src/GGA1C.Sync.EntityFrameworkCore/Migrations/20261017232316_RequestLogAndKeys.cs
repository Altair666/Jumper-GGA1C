using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GGA1C.Sync.Migrations
{
    /// <inheritdoc />
    public partial class RequestLogAndKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppProjectMaps_AltiumProjectId",
                table: "AppProjectMaps");

            migrationBuilder.DropIndex(
                name: "IX_AppComponentMaps_AltiumId",
                table: "AppComponentMaps");

            migrationBuilder.DropIndex(
                name: "IX_AppComponentMaps_AltiumItemGuid",
                table: "AppComponentMaps");

            migrationBuilder.CreateTable(
                name: "AppRequestLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Headers = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppRequestLog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppProjectMaps_AltiumProjectId_AltiumRevision",
                table: "AppProjectMaps",
                columns: new[] { "AltiumProjectId", "AltiumRevision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppComponentMaps_AltiumId",
                table: "AppComponentMaps",
                column: "AltiumId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppComponentMaps_AltiumItemGuid",
                table: "AppComponentMaps",
                column: "AltiumItemGuid");

            migrationBuilder.CreateIndex(
                name: "IX_AppRequestLog_Id",
                table: "AppRequestLog",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppRequestLog");

            migrationBuilder.DropIndex(
                name: "IX_AppProjectMaps_AltiumProjectId_AltiumRevision",
                table: "AppProjectMaps");

            migrationBuilder.DropIndex(
                name: "IX_AppComponentMaps_AltiumId",
                table: "AppComponentMaps");

            migrationBuilder.DropIndex(
                name: "IX_AppComponentMaps_AltiumItemGuid",
                table: "AppComponentMaps");

            migrationBuilder.CreateIndex(
                name: "IX_AppProjectMaps_AltiumProjectId",
                table: "AppProjectMaps",
                column: "AltiumProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppComponentMaps_AltiumId",
                table: "AppComponentMaps",
                column: "AltiumId");

            migrationBuilder.CreateIndex(
                name: "IX_AppComponentMaps_AltiumItemGuid",
                table: "AppComponentMaps",
                column: "AltiumItemGuid",
                unique: true);
        }
    }
}
