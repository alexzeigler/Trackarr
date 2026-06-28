using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class _202606061930 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Episodes_Series_SeriesId",
                table: "Episodes");

            migrationBuilder.DropIndex(
                name: "IX_Episodes_SeriesId_SeasonNumber_EpisodeNumber",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "Episodes");

            migrationBuilder.AddColumn<int>(
                name: "SeriesDbId",
                table: "Episodes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SeriesDbId_SeasonNumber_EpisodeNumber",
                table: "Episodes",
                columns: new[] { "SeriesDbId", "SeasonNumber", "EpisodeNumber" });

            migrationBuilder.AddForeignKey(
                name: "FK_Episodes_Series_SeriesDbId",
                table: "Episodes",
                column: "SeriesDbId",
                principalTable: "Series",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Episodes_Series_SeriesDbId",
                table: "Episodes");

            migrationBuilder.DropIndex(
                name: "IX_Episodes_SeriesDbId_SeasonNumber_EpisodeNumber",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "SeriesDbId",
                table: "Episodes");

            migrationBuilder.AddColumn<int>(
                name: "SeriesId",
                table: "Episodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SeriesId_SeasonNumber_EpisodeNumber",
                table: "Episodes",
                columns: new[] { "SeriesId", "SeasonNumber", "EpisodeNumber" });

            migrationBuilder.AddForeignKey(
                name: "FK_Episodes_Series_SeriesId",
                table: "Episodes",
                column: "SeriesId",
                principalTable: "Series",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
