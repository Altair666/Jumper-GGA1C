using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GGA1C.Sync.Migrations
{
    /// <inheritdoc />
    public partial class LabelClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppLabelClients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    KeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    KeyPrefix = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    KeyEncrypted = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppLabelClients", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppLabelClients_KeyHash",
                table: "AppLabelClients",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppLabelClients_Slug",
                table: "AppLabelClients",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppLabelClients");
        }
    }
}
