using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class _202605260757 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DownloadQueue",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RemoteId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    DownloadId = table.Column<string>(type: "TEXT", nullable: true),
                    Protocol = table.Column<string>(type: "TEXT", nullable: true),
                    DownloadClient = table.Column<string>(type: "TEXT", nullable: true),
                    DownloadClientHasPostImportCategory = table.Column<bool>(type: "INTEGER", nullable: false),
                    Indexer = table.Column<string>(type: "TEXT", nullable: true),
                    OutputPath = table.Column<string>(type: "TEXT", nullable: true),
                    EstimatedCompletionTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Added = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CustomFormatScore = table.Column<int>(type: "INTEGER", nullable: false),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentServerId = table.Column<int>(type: "INTEGER", nullable: true),
                    SizeLeft = table.Column<long>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    QueueStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadQueue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadQueue_MediaServers_ParentServerId",
                        column: x => x.ParentServerId,
                        principalTable: "MediaServers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadQueue_ParentServerId",
                table: "DownloadQueue",
                column: "ParentServerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadQueue");
        }
    }
}
