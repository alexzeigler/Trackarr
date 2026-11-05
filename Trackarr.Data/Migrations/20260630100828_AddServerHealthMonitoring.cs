using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServerHealthMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DisabledByHeartbeat",
                table: "MediaServers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "UptimePercent",
                table: "MediaServers",
                type: "REAL",
                nullable: false,
                defaultValue: 100.0);

            migrationBuilder.CreateTable(
                name: "ServerHealthEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerHealthEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServerHealthEvents_MediaServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "MediaServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServerStatusSamples",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    SampledAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PingMs = table.Column<long>(type: "INTEGER", nullable: true),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerStatusSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServerStatusSamples_MediaServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "MediaServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServerHealthEvents_EventType",
                table: "ServerHealthEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_ServerHealthEvents_OccurredAt",
                table: "ServerHealthEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_ServerHealthEvents_ServerId",
                table: "ServerHealthEvents",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerStatusSamples_SampledAt",
                table: "ServerStatusSamples",
                column: "SampledAt");

            migrationBuilder.CreateIndex(
                name: "IX_ServerStatusSamples_ServerId",
                table: "ServerStatusSamples",
                column: "ServerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerHealthEvents");

            migrationBuilder.DropTable(
                name: "ServerStatusSamples");

            migrationBuilder.DropColumn(
                name: "DisabledByHeartbeat",
                table: "MediaServers");

            migrationBuilder.DropColumn(
                name: "UptimePercent",
                table: "MediaServers");
        }
    }
}
