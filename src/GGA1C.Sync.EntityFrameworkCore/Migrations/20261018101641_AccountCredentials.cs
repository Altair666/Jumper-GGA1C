using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GGA1C.Sync.Migrations
{
    /// <inheritdoc />
    public partial class AccountCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AltiumLogin",
                table: "AppLabelClients",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AltiumPasswordEncrypted",
                table: "AppLabelClients",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CredentialsUpdatedAt",
                table: "AppLabelClients",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PdmLogin",
                table: "AppLabelClients",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PdmPasswordEncrypted",
                table: "AppLabelClients",
                type: "TEXT",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AltiumLogin",
                table: "AppLabelClients");

            migrationBuilder.DropColumn(
                name: "AltiumPasswordEncrypted",
                table: "AppLabelClients");

            migrationBuilder.DropColumn(
                name: "CredentialsUpdatedAt",
                table: "AppLabelClients");

            migrationBuilder.DropColumn(
                name: "PdmLogin",
                table: "AppLabelClients");

            migrationBuilder.DropColumn(
                name: "PdmPasswordEncrypted",
                table: "AppLabelClients");
        }
    }
}
