using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GGA1C.Sync.Migrations
{
    /// <inheritdoc />
    public partial class KeycloakLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "AppLabelClients",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeycloakSub",
                table: "AppLabelClients",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeycloakUsername",
                table: "AppLabelClients",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SyncDisabledAt",
                table: "AppLabelClients",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SyncedAt",
                table: "AppLabelClients",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppLabelClients_KeycloakSub",
                table: "AppLabelClients",
                column: "KeycloakSub",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppLabelClients_KeycloakSub",
                table: "AppLabelClients");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "AppLabelClients");

            migrationBuilder.DropColumn(
                name: "KeycloakSub",
                table: "AppLabelClients");

            migrationBuilder.DropColumn(
                name: "KeycloakUsername",
                table: "AppLabelClients");

            migrationBuilder.DropColumn(
                name: "SyncDisabledAt",
                table: "AppLabelClients");

            migrationBuilder.DropColumn(
                name: "SyncedAt",
                table: "AppLabelClients");
        }
    }
}
