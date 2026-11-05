using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAttemptMissedScans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MissedScans",
                table: "DownloadAttempts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MissedScans",
                table: "DownloadAttempts");
        }
    }
}
