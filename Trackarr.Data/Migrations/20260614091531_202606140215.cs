using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class _202606140215 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Episodes_SeriesDbId_SeasonNumber_EpisodeNumber",
                table: "Episodes");

            migrationBuilder.RenameColumn(
                name: "RatingsJson",
                table: "Episodes",
                newName: "SeriesJson");

            migrationBuilder.RenameColumn(
                name: "LanguagesJson",
                table: "Episodes",
                newName: "GrabDate");

            migrationBuilder.AlterColumn<int>(
                name: "AbsoluteEpisodeNumber",
                table: "Episodes",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<DateTime>(
                name: "EndTime",
                table: "Episodes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinaleType",
                table: "Episodes",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Runtime",
                table: "Episodes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SceneAbsoluteEpisodeNumber",
                table: "Episodes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SceneEpisodeNumber",
                table: "Episodes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SceneSeasonNumber",
                table: "Episodes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SeriesDbId",
                table: "Episodes",
                column: "SeriesDbId");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_ServerId_RemoteSeriesId_SeasonNumber_EpisodeNumber",
                table: "Episodes",
                columns: new[] { "ServerId", "RemoteSeriesId", "SeasonNumber", "EpisodeNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_ServerId_SeriesDbId",
                table: "Episodes",
                columns: new[] { "ServerId", "SeriesDbId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Episodes_SeriesDbId",
                table: "Episodes");

            migrationBuilder.DropIndex(
                name: "IX_Episodes_ServerId_RemoteSeriesId_SeasonNumber_EpisodeNumber",
                table: "Episodes");

            migrationBuilder.DropIndex(
                name: "IX_Episodes_ServerId_SeriesDbId",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "FinaleType",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "Runtime",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "SceneAbsoluteEpisodeNumber",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "SceneEpisodeNumber",
                table: "Episodes");

            migrationBuilder.DropColumn(
                name: "SceneSeasonNumber",
                table: "Episodes");

            migrationBuilder.RenameColumn(
                name: "SeriesJson",
                table: "Episodes",
                newName: "RatingsJson");

            migrationBuilder.RenameColumn(
                name: "GrabDate",
                table: "Episodes",
                newName: "LanguagesJson");

            migrationBuilder.AlterColumn<int>(
                name: "AbsoluteEpisodeNumber",
                table: "Episodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SeriesDbId_SeasonNumber_EpisodeNumber",
                table: "Episodes",
                columns: new[] { "SeriesDbId", "SeasonNumber", "EpisodeNumber" });
        }
    }
}
