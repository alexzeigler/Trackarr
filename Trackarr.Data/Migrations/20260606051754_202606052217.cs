using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class _202606052217 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomFormatsJson",
                table: "DownloadQueue",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LanguagesJson",
                table: "DownloadQueue",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QualityJson",
                table: "DownloadQueue",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusMessagesJson",
                table: "DownloadQueue",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeLeft",
                table: "DownloadQueue",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackedDownloadState",
                table: "DownloadQueue",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackedDownloadStatus",
                table: "DownloadQueue",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomFormatsJson",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "LanguagesJson",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "QualityJson",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "StatusMessagesJson",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "TimeLeft",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "TrackedDownloadState",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "TrackedDownloadStatus",
                table: "DownloadQueue");
        }
    }
}
