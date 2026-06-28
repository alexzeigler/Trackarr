using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServerBehaviorFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoRemoveDownloads",
                table: "MediaServers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MonitorQueue",
                table: "MediaServers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "MediaServers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RequestTimeoutSeconds",
                table: "MediaServers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SearchMissing",
                table: "MediaServers",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoRemoveDownloads",
                table: "MediaServers");

            migrationBuilder.DropColumn(
                name: "MonitorQueue",
                table: "MediaServers");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "MediaServers");

            migrationBuilder.DropColumn(
                name: "RequestTimeoutSeconds",
                table: "MediaServers");

            migrationBuilder.DropColumn(
                name: "SearchMissing",
                table: "MediaServers");
        }
    }
}
