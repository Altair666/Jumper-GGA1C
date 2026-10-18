using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace GGA1C.Sync.Migrations
{
    /// <inheritdoc />
    public partial class LabelsInDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PrintLogin",
                table: "AppLabelClients",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppLabels",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Slug = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ExternalId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Date = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ProductCode = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    ModeCode = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Article = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Manufacturer = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Barcode = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Qty = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Extra1 = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Extra2 = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Extra3 = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppLabels", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AppLabelClients_PrintLogin",
                table: "AppLabelClients",
                column: "PrintLogin",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppLabels_Slug",
                table: "AppLabels",
                column: "Slug");

            migrationBuilder.CreateIndex(
                name: "IX_AppLabels_Slug_ExternalId",
                table: "AppLabels",
                columns: new[] { "Slug", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppLabels");

            migrationBuilder.DropIndex(
                name: "IX_AppLabelClients_PrintLogin",
                table: "AppLabelClients");

            migrationBuilder.DropColumn(
                name: "PrintLogin",
                table: "AppLabelClients");
        }
    }
}
