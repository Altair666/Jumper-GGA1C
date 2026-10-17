using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GGA1C.Sync.Migrations
{
    /// <inheritdoc />
    public partial class AltiumMaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppComponentMaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AltiumId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    AltiumItemGuid = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AltiumName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AltiumPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Article = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Manufacturer = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Name1C = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    OnecGuid = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OnecName = table.Column<string>(type: "TEXT", nullable: false),
                    SyncStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    SyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppComponentMaps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppFolderMaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AltiumFolderId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AltiumFolderPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    OnecGroupGuid = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OnecGroupPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    AutoCreated = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppFolderMaps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppProjectMaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AltiumProjectId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    AltiumProjectName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AltiumRevision = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OnecNomenclatureGuid = table.Column<string>(type: "TEXT", nullable: false),
                    OnecSpecGuid = table.Column<string>(type: "TEXT", nullable: false),
                    SyncAction = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SyncStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    SyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppProjectMaps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSyncLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EntityName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSyncLog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppComponentMaps_AltiumId",
                table: "AppComponentMaps",
                column: "AltiumId");

            migrationBuilder.CreateIndex(
                name: "IX_AppComponentMaps_AltiumItemGuid",
                table: "AppComponentMaps",
                column: "AltiumItemGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppFolderMaps_AltiumFolderId",
                table: "AppFolderMaps",
                column: "AltiumFolderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppProjectMaps_AltiumProjectId",
                table: "AppProjectMaps",
                column: "AltiumProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppSyncLog_Timestamp",
                table: "AppSyncLog",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppComponentMaps");

            migrationBuilder.DropTable(
                name: "AppFolderMaps");

            migrationBuilder.DropTable(
                name: "AppProjectMaps");

            migrationBuilder.DropTable(
                name: "AppSyncLog");
        }
    }
}
