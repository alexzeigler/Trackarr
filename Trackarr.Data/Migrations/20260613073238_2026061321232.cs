using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class _2026061321232 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LastPingTime",
                table: "MediaServers",
                newName: "LastQueueCount");

            migrationBuilder.RenameColumn(
                name: "LastPingSuccessful",
                table: "MediaServers",
                newName: "LastPingMs");

            migrationBuilder.RenameColumn(
                name: "LastAuthenticated",
                table: "MediaServers",
                newName: "LastAuthenticatedAt");

            migrationBuilder.AddColumn<bool>(
                name: "IsOnline",
                table: "MediaServers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "MediaServers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastErrorAt",
                table: "MediaServers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastMissingCount",
                table: "MediaServers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastMissingFetchAt",
                table: "MediaServers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastPingedAt",
                table: "MediaServers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastQueueFetchAt",
                table: "MediaServers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsOnline",
                table: "MediaServers");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "MediaServers");

            migrationBuilder.DropColumn(
                name: "LastErrorAt",
                table: "MediaServers");

            migrationBuilder.DropColumn(
                name: "LastMissingCount",
                table: "MediaServers");

            migrationBuilder.DropColumn(
                name: "LastMissingFetchAt",
                table: "MediaServers");

            migrationBuilder.DropColumn(
                name: "LastPingedAt",
                table: "MediaServers");

            migrationBuilder.DropColumn(
                name: "LastQueueFetchAt",
                table: "MediaServers");

            migrationBuilder.RenameColumn(
                name: "LastQueueCount",
                table: "MediaServers",
                newName: "LastPingTime");

            migrationBuilder.RenameColumn(
                name: "LastPingMs",
                table: "MediaServers",
                newName: "LastPingSuccessful");

            migrationBuilder.RenameColumn(
                name: "LastAuthenticatedAt",
                table: "MediaServers",
                newName: "LastAuthenticated");
        }
    }
}
