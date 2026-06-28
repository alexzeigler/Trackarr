using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class _202606192033 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FallbackProfile",
                table: "MediaServers");

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastQueueFetchAt",
                table: "MediaServers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FallBackProfileId",
                table: "MediaServers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FallBackProfileId",
                table: "MediaServers");

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastQueueFetchAt",
                table: "MediaServers",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "FallbackProfile",
                table: "MediaServers",
                type: "TEXT",
                maxLength: 255,
                nullable: true);
        }
    }
}
