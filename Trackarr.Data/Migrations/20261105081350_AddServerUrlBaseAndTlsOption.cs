using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServerUrlBaseAndTlsOption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SkipCertificateValidation",
                table: "MediaServers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "UrlBase",
                table: "MediaServers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SkipCertificateValidation",
                table: "MediaServers");

            migrationBuilder.DropColumn(
                name: "UrlBase",
                table: "MediaServers");
        }
    }
}
