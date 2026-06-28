using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class _202606132111 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadStrikes");

            migrationBuilder.AlterColumn<string>(
                name: "ServiceType",
                table: "MediaServers",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.CreateTable(
                name: "DownloadAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServiceType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DownloadId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    RemoteId = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    NormalizedTitle = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ReleaseKey = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DownloadClient = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Indexer = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    MediaType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RemovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReplacedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FinalReasonKey = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    FinalMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ReplacementAttemptNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    WasRemovedByTrackarr = table.Column<bool>(type: "INTEGER", nullable: false),
                    WasImported = table.Column<bool>(type: "INTEGER", nullable: false),
                    WasSuccessful = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsProtected = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadAttempts_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DownloadAttempts_MediaServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "MediaServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DownloadAttempts_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DownloadAttempts_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MediaHealthSummaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ActiveAttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SuccessCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RemovalCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ReplacementCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSuccessAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastFailureAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastRemovalAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastReplacementAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CurrentState = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CurrentBestScore = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentWorstScore = table.Column<int>(type: "INTEGER", nullable: false),
                    SearchAttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    QualityDowngradeCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastQualityProfile = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    LowestQualityTried = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    IsStuck = table.Column<bool>(type: "INTEGER", nullable: false),
                    StuckReasonKey = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    StuckReasonMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    StuckSince = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaHealthSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaHealthSummaries_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaHealthSummaries_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaHealthSummaries_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SourceHealthSummaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: true),
                    ServiceType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Indexer = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    DownloadClient = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ReleaseKey = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ReasonKey = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RecoveryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SuccessCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RemovalCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AverageScore = table.Column<decimal>(type: "TEXT", precision: 8, scale: 2, nullable: false),
                    WorstScore = table.Column<int>(type: "INTEGER", nullable: false),
                    BestScore = table.Column<int>(type: "INTEGER", nullable: false),
                    ReputationScore = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastFailureAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSuccessAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceHealthSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceHealthSummaries_MediaServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "MediaServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "HealthEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DownloadAttemptId = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ReasonKey = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ScoreBefore = table.Column<decimal>(type: "TEXT", precision: 8, scale: 2, nullable: false),
                    ScoreDelta = table.Column<decimal>(type: "TEXT", precision: 8, scale: 2, nullable: false),
                    ScoreAfter = table.Column<decimal>(type: "TEXT", precision: 8, scale: 2, nullable: false),
                    ConfidenceBefore = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfidenceDelta = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfidenceAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    RecoveryCreditBefore = table.Column<int>(type: "INTEGER", nullable: false),
                    RecoveryCreditDelta = table.Column<int>(type: "INTEGER", nullable: false),
                    RecoveryCreditAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    IsGoodObservation = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBadObservation = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsMeaningfulRecovery = table.Column<bool>(type: "INTEGER", nullable: false),
                    CountsTowardRemoval = table.Column<bool>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RawStatus = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RawTrackedDownloadStatus = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RawTrackedDownloadState = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RawErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealthEvents_DownloadAttempts_DownloadAttemptId",
                        column: x => x.DownloadAttemptId,
                        principalTable: "DownloadAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HealthSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DownloadAttemptId = table.Column<int>(type: "INTEGER", nullable: false),
                    RawScore = table.Column<decimal>(type: "TEXT", precision: 8, scale: 2, nullable: false),
                    VisibleScore = table.Column<int>(type: "INTEGER", nullable: false),
                    Confidence = table.Column<int>(type: "INTEGER", nullable: false),
                    RecoveryCredit = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    GoodScanCount = table.Column<int>(type: "INTEGER", nullable: false),
                    BadScanCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ConsecutiveGoodScans = table.Column<int>(type: "INTEGER", nullable: false),
                    ConsecutiveBadScans = table.Column<int>(type: "INTEGER", nullable: false),
                    BadScansInLastTen = table.Column<int>(type: "INTEGER", nullable: false),
                    GoodScansInLastTen = table.Column<int>(type: "INTEGER", nullable: false),
                    LastProgress = table.Column<decimal>(type: "TEXT", precision: 8, scale: 2, nullable: true),
                    LastSizeLeft = table.Column<long>(type: "INTEGER", nullable: true),
                    LastSize = table.Column<long>(type: "INTEGER", nullable: true),
                    LastStatus = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LastTrackedDownloadStatus = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LastTrackedDownloadState = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LastErrorKey = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    LastObservationAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastGoodObservationAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastBadObservationAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastMeaningfulRecoveryAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastScoreChange = table.Column<decimal>(type: "TEXT", precision: 8, scale: 2, nullable: false),
                    LastScoreChangedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastReasonKey = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    LastReasonMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsEligibleForRemoval = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRemovalRestricted = table.Column<bool>(type: "INTEGER", nullable: false),
                    RemovalRestrictionReason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HealthSnapshots_DownloadAttempts_DownloadAttemptId",
                        column: x => x.DownloadAttemptId,
                        principalTable: "DownloadAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Series_CleanTitle",
                table: "Series",
                column: "CleanTitle");

            migrationBuilder.CreateIndex(
                name: "IX_Series_Title",
                table: "Series",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_CleanTitle",
                table: "Movies",
                column: "CleanTitle");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_Title",
                table: "Movies",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_LogEvents_LogLevel",
                table: "LogEvents",
                column: "LogLevel");

            migrationBuilder.CreateIndex(
                name: "IX_LogEvents_Source",
                table: "LogEvents",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_LogEvents_TimeStamp",
                table: "LogEvents",
                column: "TimeStamp");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadQueue_ServerId_DownloadId",
                table: "DownloadQueue",
                columns: new[] { "ServerId", "DownloadId" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadAttempts_EpisodeId",
                table: "DownloadAttempts",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadAttempts_FirstSeenAt",
                table: "DownloadAttempts",
                column: "FirstSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadAttempts_LastSeenAt",
                table: "DownloadAttempts",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadAttempts_MediaType_EpisodeId",
                table: "DownloadAttempts",
                columns: new[] { "MediaType", "EpisodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadAttempts_MediaType_MovieId",
                table: "DownloadAttempts",
                columns: new[] { "MediaType", "MovieId" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadAttempts_MediaType_SeriesId",
                table: "DownloadAttempts",
                columns: new[] { "MediaType", "SeriesId" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadAttempts_MovieId",
                table: "DownloadAttempts",
                column: "MovieId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadAttempts_SeriesId",
                table: "DownloadAttempts",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadAttempts_ServerId_DownloadId",
                table: "DownloadAttempts",
                columns: new[] { "ServerId", "DownloadId" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadAttempts_ServerId_DownloadId_Status",
                table: "DownloadAttempts",
                columns: new[] { "ServerId", "DownloadId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadAttempts_ServerId_ReleaseKey",
                table: "DownloadAttempts",
                columns: new[] { "ServerId", "ReleaseKey" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadAttempts_Status",
                table: "DownloadAttempts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_HealthEvents_CountsTowardRemoval",
                table: "HealthEvents",
                column: "CountsTowardRemoval");

            migrationBuilder.CreateIndex(
                name: "IX_HealthEvents_DownloadAttemptId",
                table: "HealthEvents",
                column: "DownloadAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_HealthEvents_EventType",
                table: "HealthEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_HealthEvents_IsBadObservation",
                table: "HealthEvents",
                column: "IsBadObservation");

            migrationBuilder.CreateIndex(
                name: "IX_HealthEvents_IsGoodObservation",
                table: "HealthEvents",
                column: "IsGoodObservation");

            migrationBuilder.CreateIndex(
                name: "IX_HealthEvents_IsMeaningfulRecovery",
                table: "HealthEvents",
                column: "IsMeaningfulRecovery");

            migrationBuilder.CreateIndex(
                name: "IX_HealthEvents_OccurredAt",
                table: "HealthEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_HealthEvents_ReasonKey",
                table: "HealthEvents",
                column: "ReasonKey");

            migrationBuilder.CreateIndex(
                name: "IX_HealthEvents_Severity",
                table: "HealthEvents",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_HealthSnapshots_Confidence",
                table: "HealthSnapshots",
                column: "Confidence");

            migrationBuilder.CreateIndex(
                name: "IX_HealthSnapshots_DownloadAttemptId",
                table: "HealthSnapshots",
                column: "DownloadAttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealthSnapshots_IsEligibleForRemoval",
                table: "HealthSnapshots",
                column: "IsEligibleForRemoval");

            migrationBuilder.CreateIndex(
                name: "IX_HealthSnapshots_LastBadObservationAt",
                table: "HealthSnapshots",
                column: "LastBadObservationAt");

            migrationBuilder.CreateIndex(
                name: "IX_HealthSnapshots_LastMeaningfulRecoveryAt",
                table: "HealthSnapshots",
                column: "LastMeaningfulRecoveryAt");

            migrationBuilder.CreateIndex(
                name: "IX_HealthSnapshots_LastObservationAt",
                table: "HealthSnapshots",
                column: "LastObservationAt");

            migrationBuilder.CreateIndex(
                name: "IX_HealthSnapshots_State",
                table: "HealthSnapshots",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_HealthSnapshots_VisibleScore",
                table: "HealthSnapshots",
                column: "VisibleScore");

            migrationBuilder.CreateIndex(
                name: "IX_MediaHealthSummaries_CurrentState",
                table: "MediaHealthSummaries",
                column: "CurrentState");

            migrationBuilder.CreateIndex(
                name: "IX_MediaHealthSummaries_EpisodeId",
                table: "MediaHealthSummaries",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaHealthSummaries_IsStuck",
                table: "MediaHealthSummaries",
                column: "IsStuck");

            migrationBuilder.CreateIndex(
                name: "IX_MediaHealthSummaries_LastAttemptAt",
                table: "MediaHealthSummaries",
                column: "LastAttemptAt");

            migrationBuilder.CreateIndex(
                name: "IX_MediaHealthSummaries_LastFailureAt",
                table: "MediaHealthSummaries",
                column: "LastFailureAt");

            migrationBuilder.CreateIndex(
                name: "IX_MediaHealthSummaries_MediaType_EpisodeId",
                table: "MediaHealthSummaries",
                columns: new[] { "MediaType", "EpisodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaHealthSummaries_MediaType_MovieId",
                table: "MediaHealthSummaries",
                columns: new[] { "MediaType", "MovieId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaHealthSummaries_MediaType_SeriesId",
                table: "MediaHealthSummaries",
                columns: new[] { "MediaType", "SeriesId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaHealthSummaries_MovieId",
                table: "MediaHealthSummaries",
                column: "MovieId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaHealthSummaries_SeriesId",
                table: "MediaHealthSummaries",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaHealthSummaries_StuckSince",
                table: "MediaHealthSummaries",
                column: "StuckSince");

            migrationBuilder.CreateIndex(
                name: "IX_SourceHealthSummaries_LastFailureAt",
                table: "SourceHealthSummaries",
                column: "LastFailureAt");

            migrationBuilder.CreateIndex(
                name: "IX_SourceHealthSummaries_LastSeenAt",
                table: "SourceHealthSummaries",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_SourceHealthSummaries_LastSuccessAt",
                table: "SourceHealthSummaries",
                column: "LastSuccessAt");

            migrationBuilder.CreateIndex(
                name: "IX_SourceHealthSummaries_ReputationScore",
                table: "SourceHealthSummaries",
                column: "ReputationScore");

            migrationBuilder.CreateIndex(
                name: "IX_SourceHealthSummaries_ServerId_ServiceType_Indexer_DownloadClient_Protocol_ReleaseKey_ReasonKey",
                table: "SourceHealthSummaries",
                columns: new[] { "ServerId", "ServiceType", "Indexer", "DownloadClient", "Protocol", "ReleaseKey", "ReasonKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HealthEvents");

            migrationBuilder.DropTable(
                name: "HealthSnapshots");

            migrationBuilder.DropTable(
                name: "MediaHealthSummaries");

            migrationBuilder.DropTable(
                name: "SourceHealthSummaries");

            migrationBuilder.DropTable(
                name: "DownloadAttempts");

            migrationBuilder.DropIndex(
                name: "IX_Series_CleanTitle",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_Series_Title",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_Movies_CleanTitle",
                table: "Movies");

            migrationBuilder.DropIndex(
                name: "IX_Movies_Title",
                table: "Movies");

            migrationBuilder.DropIndex(
                name: "IX_LogEvents_LogLevel",
                table: "LogEvents");

            migrationBuilder.DropIndex(
                name: "IX_LogEvents_Source",
                table: "LogEvents");

            migrationBuilder.DropIndex(
                name: "IX_LogEvents_TimeStamp",
                table: "LogEvents");

            migrationBuilder.DropIndex(
                name: "IX_DownloadQueue_ServerId_DownloadId",
                table: "DownloadQueue");

            migrationBuilder.AlterColumn<int>(
                name: "ServiceType",
                table: "MediaServers",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 50);

            migrationBuilder.CreateTable(
                name: "DownloadStrikes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DownloadItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    CountsAgainstDownload = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ObservedDownloadClient = table.Column<string>(type: "TEXT", nullable: true),
                    ObservedDownloadId = table.Column<string>(type: "TEXT", nullable: true),
                    ObservedErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    ObservedIndexer = table.Column<string>(type: "TEXT", nullable: true),
                    ObservedProgress = table.Column<decimal>(type: "TEXT", nullable: true),
                    ObservedSize = table.Column<long>(type: "INTEGER", nullable: true),
                    ObservedSizeLeft = table.Column<long>(type: "INTEGER", nullable: true),
                    ObservedStatus = table.Column<string>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    Resolution = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Severity = table.Column<string>(type: "TEXT", nullable: false)
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
                name: "IX_DownloadStrikes_DownloadItemId",
                table: "DownloadStrikes",
                column: "DownloadItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadStrikes_DownloadItemId_Reason_Fingerprint_ResolvedAtUtc",
                table: "DownloadStrikes",
                columns: new[] { "DownloadItemId", "Reason", "Fingerprint", "ResolvedAtUtc" });
        }
    }
}
