using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class _202606052251 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DownloadQueue_MediaServers_ParentServerId",
                table: "DownloadQueue");

            migrationBuilder.DropIndex(
                name: "IX_DownloadQueue_ParentServerId",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "ParentServerId",
                table: "DownloadQueue");

            migrationBuilder.CreateTable(
                name: "Movies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RemoteId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    OriginalTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SortTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Overview = table.Column<string>(type: "TEXT", nullable: true),
                    Website = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    YouTubeTrailerId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Studio = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    QualityProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    HasFile = table.Column<bool>(type: "INTEGER", nullable: false),
                    MovieFileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Monitored = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinimumAvailability = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    IsAvailable = table.Column<bool>(type: "INTEGER", nullable: false),
                    FolderName = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Runtime = table.Column<int>(type: "INTEGER", nullable: false),
                    CleanTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ImdbId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    TitleSlug = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Certification = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    SizeOnDisk = table.Column<long>(type: "INTEGER", nullable: false),
                    InCinemas = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PhysicalRelease = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DigitalRelease = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReleaseDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Added = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSearchTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Popularity = table.Column<double>(type: "REAL", nullable: false),
                    OriginalLanguageJson = table.Column<string>(type: "TEXT", nullable: true),
                    AlternateTitlesJson = table.Column<string>(type: "TEXT", nullable: true),
                    ImagesJson = table.Column<string>(type: "TEXT", nullable: true),
                    GenresJson = table.Column<string>(type: "TEXT", nullable: true),
                    KeywordsJson = table.Column<string>(type: "TEXT", nullable: true),
                    TagsJson = table.Column<string>(type: "TEXT", nullable: true),
                    RatingsJson = table.Column<string>(type: "TEXT", nullable: true),
                    CollectionJson = table.Column<string>(type: "TEXT", nullable: true),
                    StatisticsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Movies_MediaServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "MediaServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Movies_ServerId_RemoteId",
                table: "Movies",
                columns: new[] { "ServerId", "RemoteId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Movies");

            migrationBuilder.AddColumn<int>(
                name: "ParentServerId",
                table: "DownloadQueue",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DownloadQueue_ParentServerId",
                table: "DownloadQueue",
                column: "ParentServerId");

            migrationBuilder.AddForeignKey(
                name: "FK_DownloadQueue_MediaServers_ParentServerId",
                table: "DownloadQueue",
                column: "ParentServerId",
                principalTable: "MediaServers",
                principalColumn: "Id");
        }
    }
}
