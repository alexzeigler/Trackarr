using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScanCountAndMissingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScanCount",
                table: "HealthEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1); // every existing event stands for at least one scan

            // Duplicate movie/series rows (EpisodeId null) slipped past the old index; keep the oldest of each
            // before adding the unique index, or creating it would fail.
            migrationBuilder.Sql(
                "DELETE FROM \"MissingMedia\" WHERE \"EpisodeId\" IS NULL AND \"Id\" NOT IN " +
                "(SELECT MIN(\"Id\") FROM \"MissingMedia\" WHERE \"EpisodeId\" IS NULL GROUP BY \"ServerId\", \"RemoteId\");");

            migrationBuilder.CreateIndex(
                name: "IX_MissingMedia_ServerId_RemoteId",
                table: "MissingMedia",
                columns: new[] { "ServerId", "RemoteId" },
                unique: true,
                filter: "\"EpisodeId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MissingMedia_ServerId_RemoteId",
                table: "MissingMedia");

            migrationBuilder.DropColumn(
                name: "ScanCount",
                table: "HealthEvents");
        }
    }
}
