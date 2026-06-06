using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaServers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ServiceType = table.Column<int>(type: "INTEGER", nullable: false),
                    Host = table.Column<string>(type: "TEXT", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    UseHttps = table.Column<bool>(type: "INTEGER", nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    FallbackProfile = table.Column<string>(type: "TEXT", nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastAuthenticated = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastPingTime = table.Column<long>(type: "INTEGER", nullable: true),
                    LastPingSuccessful = table.Column<bool>(type: "INTEGER", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsAuthenticated = table.Column<bool>(type: "INTEGER", nullable: false),
                    DownloadsProcessed = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaServers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServerMetadata",
                columns: table => new
                {
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    AppName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    InstanceName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    BuildTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsDebug = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsProduction = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsUserInteractive = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartupPath = table.Column<string>(type: "TEXT", nullable: false),
                    AppData = table.Column<string>(type: "TEXT", nullable: false),
                    OsName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    OsVersion = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    IsNetCore = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsLinux = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsOsx = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsWindows = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDocker = table.Column<bool>(type: "INTEGER", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DatabaseType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DatabaseVersion = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Authentication = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    MigrationVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    UrlBase = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    RuntimeVersion = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    RuntimeName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PackageVersion = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PackageAuthor = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    PackageUpdateMechanism = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PackageUpdateMechanismMessage = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerMetadata", x => x.ServerId);
                    table.ForeignKey(
                        name: "FK_ServerMetadata_MediaServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "MediaServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerMetadata");

            migrationBuilder.DropTable(
                name: "MediaServers");
        }
    }
}
