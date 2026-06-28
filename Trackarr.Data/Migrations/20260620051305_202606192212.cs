using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class _202606192212 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "FallBackProfileId",
                table: "MediaServers",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.CreateIndex(
                name: "IX_MediaServers_FallBackProfileId",
                table: "MediaServers",
                column: "FallBackProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_MediaServers_QualityProfiles_FallBackProfileId",
                table: "MediaServers",
                column: "FallBackProfileId",
                principalTable: "QualityProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaServers_QualityProfiles_FallBackProfileId",
                table: "MediaServers");

            migrationBuilder.DropIndex(
                name: "IX_MediaServers_FallBackProfileId",
                table: "MediaServers");

            migrationBuilder.AlterColumn<int>(
                name: "FallBackProfileId",
                table: "MediaServers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
