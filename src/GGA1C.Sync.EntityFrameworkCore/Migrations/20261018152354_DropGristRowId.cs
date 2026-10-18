using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GGA1C.Sync.Migrations
{
    /// <inheritdoc />
    public partial class DropGristRowId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GristRowId",
                table: "AppLabelClients");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GristRowId",
                table: "AppLabelClients",
                type: "INTEGER",
                nullable: true);
        }
    }
}
