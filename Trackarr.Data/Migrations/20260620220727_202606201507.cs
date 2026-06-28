using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class _202606201507 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DownloadQueue_AttemptKey",
                table: "DownloadQueue");

            migrationBuilder.DropIndex(
                name: "IX_DownloadQueue_ServerId_AttemptKey",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "AttemptKey",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "FirstSeenAtUtc",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "LastObservedProgress",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "LastObservedSizeLeft",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "LastProgressAtUtc",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "LastSeenAtUtc",
                table: "DownloadQueue");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttemptKey",
                table: "DownloadQueue",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstSeenAtUtc",
                table: "DownloadQueue",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LastObservedProgress",
                table: "DownloadQueue",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastObservedSizeLeft",
                table: "DownloadQueue",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastProgressAtUtc",
                table: "DownloadQueue",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenAtUtc",
                table: "DownloadQueue",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DownloadQueue_AttemptKey",
                table: "DownloadQueue",
                column: "AttemptKey");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadQueue_ServerId_AttemptKey",
                table: "DownloadQueue",
                columns: new[] { "ServerId", "AttemptKey" });
        }
    }
}
