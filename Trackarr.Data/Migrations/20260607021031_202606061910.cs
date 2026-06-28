using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class _202606061910 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Series",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RemoteId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SortTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Ended = table.Column<bool>(type: "INTEGER", nullable: false),
                    Overview = table.Column<string>(type: "TEXT", nullable: true),
                    Network = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    AirTime = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    QualityProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    LanguageProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    SeasonFolder = table.Column<bool>(type: "INTEGER", nullable: false),
                    Monitored = table.Column<bool>(type: "INTEGER", nullable: false),
                    MonitorNewItems = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UseSceneNumbering = table.Column<bool>(type: "INTEGER", nullable: false),
                    Runtime = table.Column<int>(type: "INTEGER", nullable: false),
                    TvdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    TvRageId = table.Column<int>(type: "INTEGER", nullable: false),
                    TvMazeId = table.Column<int>(type: "INTEGER", nullable: false),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    ImdbId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    TitleSlug = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    RootFolderPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Certification = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    SeriesType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CleanTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstAired = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastAired = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextAiring = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PreviousAiring = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Added = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AlternateTitlesJson = table.Column<string>(type: "TEXT", nullable: true),
                    ImagesJson = table.Column<string>(type: "TEXT", nullable: true),
                    OriginalLanguageJson = table.Column<string>(type: "TEXT", nullable: true),
                    SeasonsJson = table.Column<string>(type: "TEXT", nullable: true),
                    GenresJson = table.Column<string>(type: "TEXT", nullable: true),
                    TagsJson = table.Column<string>(type: "TEXT", nullable: true),
                    RatingsJson = table.Column<string>(type: "TEXT", nullable: true),
                    StatisticsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Series", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Series_MediaServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "MediaServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Episodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RemoteId = table.Column<int>(type: "INTEGER", nullable: false),
                    RemoteSeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    TvdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeFileId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AbsoluteEpisodeNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AirDate = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    AirDateUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Overview = table.Column<string>(type: "TEXT", nullable: true),
                    HasFile = table.Column<bool>(type: "INTEGER", nullable: false),
                    Monitored = table.Column<bool>(type: "INTEGER", nullable: false),
                    UnverifiedSceneNumbering = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSearchTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ImagesJson = table.Column<string>(type: "TEXT", nullable: true),
                    EpisodeFileJson = table.Column<string>(type: "TEXT", nullable: true),
                    RatingsJson = table.Column<string>(type: "TEXT", nullable: true),
                    LanguagesJson = table.Column<string>(type: "TEXT", nullable: true),
                    ShowId = table.Column<int>(type: "INTEGER", nullable: true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Episodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Episodes_MediaServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "MediaServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Episodes_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SeriesId_SeasonNumber_EpisodeNumber",
                table: "Episodes",
                columns: new[] { "SeriesId", "SeasonNumber", "EpisodeNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_ServerId_RemoteId",
                table: "Episodes",
                columns: new[] { "ServerId", "RemoteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_ServerId_RemoteSeriesId",
                table: "Episodes",
                columns: new[] { "ServerId", "RemoteSeriesId" });

            migrationBuilder.CreateIndex(
                name: "IX_Series_ServerId_RemoteId",
                table: "Series",
                columns: new[] { "ServerId", "RemoteId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Episodes");

            migrationBuilder.DropTable(
                name: "Series");
        }
    }
}
