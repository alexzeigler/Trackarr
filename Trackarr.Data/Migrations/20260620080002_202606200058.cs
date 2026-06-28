using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class _202606200058 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MissingMediaEvents");

            migrationBuilder.DropTable(
                name: "MissingMediaItems");

            migrationBuilder.CreateTable(
                name: "MissingMedia",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    RemoteId = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    Tier = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SearchState = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    FailedSearchCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstSeenMissingAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSearchPushedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastEvaluatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LoweredToFallback = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MissingMedia", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    RemoteId = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Tier = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    FailedSearchCount = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MissingMedia_ServerId",
                table: "MissingMedia",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMedia_ServerId_RemoteId_EpisodeId",
                table: "MissingMedia",
                columns: new[] { "ServerId", "RemoteId", "EpisodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SearchEvents_OccurredAt",
                table: "SearchEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_SearchEvents_ServerId_RemoteId_EpisodeId",
                table: "SearchEvents",
                columns: new[] { "ServerId", "RemoteId", "EpisodeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MissingMedia");

            migrationBuilder.DropTable(
                name: "SearchEvents");

            migrationBuilder.CreateTable(
                name: "MissingMediaItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EpisodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    AirDateUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConsecutiveMissingCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ConsecutiveSkippedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    FallbackQualityProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    FirstSeenMissingAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsMissing = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsQueued = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSearching = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSuppressed = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastAction = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAppliedQualityProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    LastDecision = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    LastEvaluatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastProfileLoweredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastQueuedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastReason = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSearchPushedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSeenMissingAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MediaType = table.Column<int>(type: "INTEGER", nullable: false),
                    ObservedQualityProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    OriginalQualityProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProfileLowerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ReleaseDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RemoteEpisodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    RemoteMovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    RemoteSeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SearchPushCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    ServiceType = table.Column<int>(type: "INTEGER", nullable: false),
                    SortTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    SuppressedUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: true)
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
                    EpisodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    MissingMediaItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Decision = table.Column<int>(type: "INTEGER", nullable: false),
                    Details = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    FallbackProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    MediaType = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ProfileAfterId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProfileBeforeId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProfileLowerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ProfileLowered = table.Column<bool>(type: "INTEGER", nullable: false),
                    Reason = table.Column<int>(type: "INTEGER", nullable: false),
                    RemoteEpisodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    RemoteMovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    RemoteSeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    SearchPushCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SearchPushed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ServiceType = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    WasMissing = table.Column<bool>(type: "INTEGER", nullable: false),
                    WasQueued = table.Column<bool>(type: "INTEGER", nullable: false),
                    WasSearching = table.Column<bool>(type: "INTEGER", nullable: false)
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
    }
}
