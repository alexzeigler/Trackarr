using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class _202606201456 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "FailedAtUtc",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "QueueStatus",
                table: "DownloadQueue");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "DownloadQueue",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FailedAtUtc",
                table: "DownloadQueue",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QueueStatus",
                table: "DownloadQueue",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}
