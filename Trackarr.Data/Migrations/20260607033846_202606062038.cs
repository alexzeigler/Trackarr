using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class _202606062038 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActiveStrikeCount",
                table: "DownloadQueue",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AttemptKey",
                table: "DownloadQueue",
                type: "TEXT",
                nullable: true);

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

            migrationBuilder.AddColumn<int>(
                name: "TotalStrikeCount",
                table: "DownloadQueue",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TrackingState",
                table: "DownloadQueue",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "DownloadStrikes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DownloadItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    ObservedStatus = table.Column<string>(type: "TEXT", nullable: true),
                    ObservedErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    ObservedProgress = table.Column<decimal>(type: "TEXT", nullable: true),
                    ObservedSize = table.Column<long>(type: "INTEGER", nullable: true),
                    ObservedSizeLeft = table.Column<long>(type: "INTEGER", nullable: true),
                    ObservedDownloadId = table.Column<string>(type: "TEXT", nullable: true),
                    ObservedDownloadClient = table.Column<string>(type: "TEXT", nullable: true),
                    ObservedIndexer = table.Column<string>(type: "TEXT", nullable: true),
                    CountsAgainstDownload = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Resolution = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadStrikes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadStrikes_DownloadQueue_DownloadItemId",
                        column: x => x.DownloadItemId,
                        principalTable: "DownloadQueue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadQueue_AttemptKey",
                table: "DownloadQueue",
                column: "AttemptKey");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadQueue_ServerId_AttemptKey",
                table: "DownloadQueue",
                columns: new[] { "ServerId", "AttemptKey" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadStrikes_DownloadItemId",
                table: "DownloadStrikes",
                column: "DownloadItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadStrikes_DownloadItemId_Reason_Fingerprint_ResolvedAtUtc",
                table: "DownloadStrikes",
                columns: new[] { "DownloadItemId", "Reason", "Fingerprint", "ResolvedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadStrikes");

            migrationBuilder.DropIndex(
                name: "IX_DownloadQueue_AttemptKey",
                table: "DownloadQueue");

            migrationBuilder.DropIndex(
                name: "IX_DownloadQueue_ServerId_AttemptKey",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "ActiveStrikeCount",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "AttemptKey",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "FailedAtUtc",
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

            migrationBuilder.DropColumn(
                name: "TotalStrikeCount",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "TrackingState",
                table: "DownloadQueue");
        }
    }
}
