using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class _202606191629 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MissingMediaItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServiceType = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaType = table.Column<int>(type: "INTEGER", nullable: false),
                    RemoteMovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    RemoteSeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    RemoteEpisodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SortTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    AirDateUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReleaseDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsMissing = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsQueued = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSearching = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSuppressed = table.Column<bool>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstSeenMissingAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenMissingAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastEvaluatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastQueuedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSearchPushedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastProfileLoweredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SuppressedUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ObservedQualityProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    OriginalQualityProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    FallbackQualityProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    LastAppliedQualityProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    SearchPushCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ProfileLowerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ConsecutiveMissingCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ConsecutiveSkippedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAction = table.Column<int>(type: "INTEGER", nullable: false),
                    LastDecision = table.Column<int>(type: "INTEGER", nullable: false),
                    LastReason = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MissingMediaItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MissingMediaItems_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MissingMediaItems_MediaServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "MediaServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MissingMediaItems_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MissingMediaItems_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MissingMediaEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServiceType = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaType = table.Column<int>(type: "INTEGER", nullable: false),
                    MissingMediaItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    RemoteMovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    RemoteSeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    RemoteEpisodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    Decision = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Details = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    WasMissing = table.Column<bool>(type: "INTEGER", nullable: false),
                    WasQueued = table.Column<bool>(type: "INTEGER", nullable: false),
                    WasSearching = table.Column<bool>(type: "INTEGER", nullable: false),
                    SearchPushed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProfileLowered = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProfileBeforeId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProfileAfterId = table.Column<int>(type: "INTEGER", nullable: true),
                    FallbackProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    SearchPushCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ProfileLowerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MissingMediaEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MissingMediaEvents_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MissingMediaEvents_MediaServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "MediaServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MissingMediaEvents_MissingMediaItems_MissingMediaItemId",
                        column: x => x.MissingMediaItemId,
                        principalTable: "MissingMediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MissingMediaEvents_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MissingMediaEvents_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaEvents_Action",
                table: "MissingMediaEvents",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaEvents_CreatedAt",
                table: "MissingMediaEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaEvents_Decision",
                table: "MissingMediaEvents",
                column: "Decision");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaEvents_EpisodeId",
                table: "MissingMediaEvents",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaEvents_MediaType",
                table: "MissingMediaEvents",
                column: "MediaType");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaEvents_MissingMediaItemId",
                table: "MissingMediaEvents",
                column: "MissingMediaItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaEvents_MovieId",
                table: "MissingMediaEvents",
                column: "MovieId");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaEvents_Reason",
                table: "MissingMediaEvents",
                column: "Reason");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaEvents_SeriesId",
                table: "MissingMediaEvents",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaEvents_ServerId",
                table: "MissingMediaEvents",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaEvents_ServerId_MediaType_RemoteEpisodeId",
                table: "MissingMediaEvents",
                columns: new[] { "ServerId", "MediaType", "RemoteEpisodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaEvents_ServerId_MediaType_RemoteMovieId",
                table: "MissingMediaEvents",
                columns: new[] { "ServerId", "MediaType", "RemoteMovieId" });

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaItems_EpisodeId",
                table: "MissingMediaItems",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaItems_IsMissing",
                table: "MissingMediaItems",
                column: "IsMissing");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaItems_LastEvaluatedAt",
                table: "MissingMediaItems",
                column: "LastEvaluatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaItems_LastSearchPushedAt",
                table: "MissingMediaItems",
                column: "LastSearchPushedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaItems_LastSeenMissingAt",
                table: "MissingMediaItems",
                column: "LastSeenMissingAt");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaItems_MediaType",
                table: "MissingMediaItems",
                column: "MediaType");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaItems_MovieId",
                table: "MissingMediaItems",
                column: "MovieId");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaItems_SeriesId",
                table: "MissingMediaItems",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaItems_ServerId",
                table: "MissingMediaItems",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaItems_ServerId_MediaType_RemoteEpisodeId",
                table: "MissingMediaItems",
                columns: new[] { "ServerId", "MediaType", "RemoteEpisodeId" },
                unique: true,
                filter: "[RemoteEpisodeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaItems_ServerId_MediaType_RemoteMovieId",
                table: "MissingMediaItems",
                columns: new[] { "ServerId", "MediaType", "RemoteMovieId" },
                unique: true,
                filter: "[RemoteMovieId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMediaItems_State",
                table: "MissingMediaItems",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MissingMediaEvents");

            migrationBuilder.DropTable(
                name: "MissingMediaItems");
        }
    }
}
