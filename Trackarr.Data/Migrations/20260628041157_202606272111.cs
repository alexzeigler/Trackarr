using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class _202606272111 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.AlterColumn<int>(
                name: "ServerId",
                table: "Series",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<int>(
                name: "ServerId",
                table: "QualityProfiles",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<int>(
                name: "ServerId",
                table: "Movies",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<int>(
                name: "ServerId",
                table: "Episodes",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<int>(
                name: "ServerId",
                table: "DownloadAttempts",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.Sql(
                "DELETE FROM \"DownloadQueue\" WHERE \"ServerId\" NOT IN (SELECT \"Id\" FROM \"MediaServers\");");
            migrationBuilder.Sql(
                "DELETE FROM \"MissingMedia\" WHERE \"ServerId\" NOT IN (SELECT \"Id\" FROM \"MediaServers\");");
            migrationBuilder.Sql(
                "DELETE FROM \"SearchEvents\" WHERE \"ServerId\" NOT IN (SELECT \"Id\" FROM \"MediaServers\");");
            migrationBuilder.Sql(
                "DELETE FROM \"DownloadAttempts\" WHERE \"ServerId\" IS NOT NULL AND \"ServerId\" NOT IN (SELECT \"Id\" FROM \"MediaServers\");");

            migrationBuilder.AddForeignKey(
                name: "FK_DownloadAttempts_MediaServers_ServerId",
                table: "DownloadAttempts",
                column: "ServerId",
                principalTable: "MediaServers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_DownloadQueue_MediaServers_ServerId",
                table: "DownloadQueue",
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
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MissingMedia_MediaServers_ServerId",
                table: "MissingMedia",
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
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_QualityProfiles_MediaServers_ServerId",
                table: "QualityProfiles",
                column: "ServerId",
                principalTable: "MediaServers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SearchEvents_MediaServers_ServerId",
                table: "SearchEvents",
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
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DownloadAttempts_MediaServers_ServerId",
                table: "DownloadAttempts");

            migrationBuilder.DropForeignKey(
                name: "FK_DownloadQueue_MediaServers_ServerId",
                table: "DownloadQueue");

            migrationBuilder.DropForeignKey(
                name: "FK_Episodes_MediaServers_ServerId",
                table: "Episodes");

            migrationBuilder.DropForeignKey(
                name: "FK_MissingMedia_MediaServers_ServerId",
                table: "MissingMedia");

            migrationBuilder.DropForeignKey(
                name: "FK_Movies_MediaServers_ServerId",
                table: "Movies");

            migrationBuilder.DropForeignKey(
                name: "FK_QualityProfiles_MediaServers_ServerId",
                table: "QualityProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_SearchEvents_MediaServers_ServerId",
                table: "SearchEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_Series_MediaServers_ServerId",
                table: "Series");

            migrationBuilder.AlterColumn<int>(
                name: "ServerId",
                table: "Series",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ServerId",
                table: "QualityProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ServerId",
                table: "Movies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ServerId",
                table: "Episodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ServerId",
                table: "DownloadAttempts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

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
        }
    }
}
