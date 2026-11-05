using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class CascadeServerDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One-time cleanup of rows a deleted server left behind (ServerId set to null by the old SetNull keys).
            // Children first, so this works whether or not foreign keys are enforced during the migration.
            migrationBuilder.Sql(
                "DELETE FROM \"HealthEvents\" WHERE \"DownloadAttemptId\" IN " +
                "(SELECT \"Id\" FROM \"DownloadAttempts\" WHERE \"ServerId\" IS NULL);");
            migrationBuilder.Sql(
                "DELETE FROM \"HealthSnapshots\" WHERE \"DownloadAttemptId\" IN " +
                "(SELECT \"Id\" FROM \"DownloadAttempts\" WHERE \"ServerId\" IS NULL);");
            migrationBuilder.Sql("DELETE FROM \"DownloadAttempts\" WHERE \"ServerId\" IS NULL;");
            migrationBuilder.Sql("DELETE FROM \"SourceHealthSummaries\" WHERE \"ServerId\" IS NULL;");
            migrationBuilder.Sql(
                "DELETE FROM \"MediaHealthSummaries\" WHERE " +
                "\"MovieId\" IN (SELECT \"Id\" FROM \"Movies\" WHERE \"ServerId\" IS NULL) OR " +
                "\"SeriesId\" IN (SELECT \"Id\" FROM \"Series\" WHERE \"ServerId\" IS NULL) OR " +
                "\"EpisodeId\" IN (SELECT \"Id\" FROM \"Episodes\" WHERE \"ServerId\" IS NULL);");
            migrationBuilder.Sql("DELETE FROM \"Episodes\" WHERE \"ServerId\" IS NULL;");
            migrationBuilder.Sql("DELETE FROM \"Series\" WHERE \"ServerId\" IS NULL;");
            migrationBuilder.Sql("DELETE FROM \"Movies\" WHERE \"ServerId\" IS NULL;");
            migrationBuilder.Sql(
                "DELETE FROM \"QualityProfiles\" WHERE \"ServerId\" IS NULL AND \"Id\" NOT IN " +
                "(SELECT \"FallBackProfileId\" FROM \"MediaServers\" WHERE \"FallBackProfileId\" IS NOT NULL);");

            migrationBuilder.DropForeignKey(
                name: "FK_DownloadAttempts_MediaServers_ServerId",
                table: "DownloadAttempts");

            migrationBuilder.DropForeignKey(
                name: "FK_Episodes_MediaServers_ServerId",
                table: "Episodes");

            migrationBuilder.DropForeignKey(
                name: "FK_Movies_MediaServers_ServerId",
                table: "Movies");

            migrationBuilder.DropForeignKey(
                name: "FK_QualityProfiles_MediaServers_ServerId",
                table: "QualityProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_Series_MediaServers_ServerId",
                table: "Series");

            migrationBuilder.DropForeignKey(
                name: "FK_SourceHealthSummaries_MediaServers_ServerId",
                table: "SourceHealthSummaries");

            migrationBuilder.AddForeignKey(
                name: "FK_DownloadAttempts_MediaServers_ServerId",
                table: "DownloadAttempts",
                column: "ServerId",
                principalTable: "MediaServers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Episodes_MediaServers_ServerId",
                table: "Episodes",
                column: "ServerId",
                principalTable: "MediaServers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Movies_MediaServers_ServerId",
                table: "Movies",
                column: "ServerId",
                principalTable: "MediaServers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_QualityProfiles_MediaServers_ServerId",
                table: "QualityProfiles",
                column: "ServerId",
                principalTable: "MediaServers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Series_MediaServers_ServerId",
                table: "Series",
                column: "ServerId",
                principalTable: "MediaServers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SourceHealthSummaries_MediaServers_ServerId",
                table: "SourceHealthSummaries",
                column: "ServerId",
                principalTable: "MediaServers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DownloadAttempts_MediaServers_ServerId",
                table: "DownloadAttempts");

            migrationBuilder.DropForeignKey(
                name: "FK_Episodes_MediaServers_ServerId",
                table: "Episodes");

            migrationBuilder.DropForeignKey(
                name: "FK_Movies_MediaServers_ServerId",
                table: "Movies");

            migrationBuilder.DropForeignKey(
                name: "FK_QualityProfiles_MediaServers_ServerId",
                table: "QualityProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_Series_MediaServers_ServerId",
                table: "Series");

            migrationBuilder.DropForeignKey(
                name: "FK_SourceHealthSummaries_MediaServers_ServerId",
                table: "SourceHealthSummaries");

            migrationBuilder.AddForeignKey(
                name: "FK_DownloadAttempts_MediaServers_ServerId",
                table: "DownloadAttempts",
                column: "ServerId",
                principalTable: "MediaServers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Episodes_MediaServers_ServerId",
                table: "Episodes",
                column: "ServerId",
                principalTable: "MediaServers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Movies_MediaServers_ServerId",
                table: "Movies",
                column: "ServerId",
                principalTable: "MediaServers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_QualityProfiles_MediaServers_ServerId",
                table: "QualityProfiles",
                column: "ServerId",
                principalTable: "MediaServers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Series_MediaServers_ServerId",
                table: "Series",
                column: "ServerId",
                principalTable: "MediaServers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SourceHealthSummaries_MediaServers_ServerId",
                table: "SourceHealthSummaries",
                column: "ServerId",
                principalTable: "MediaServers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
