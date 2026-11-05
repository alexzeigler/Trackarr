using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class RelabelMovieMissingTier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Movie records used to be stored with the Series tier. Relabel those on Radarr servers.
            migrationBuilder.Sql(
                "UPDATE \"MissingMedia\" SET \"Tier\" = 'Movie' WHERE \"Tier\" = 'Series' AND \"ServerId\" IN " +
                "(SELECT \"Id\" FROM \"MediaServers\" WHERE \"ServiceType\" = 'Radarr');");
            migrationBuilder.Sql(
                "UPDATE \"SearchEvents\" SET \"Tier\" = 'Movie' WHERE \"Tier\" = 'Series' AND \"ServerId\" IN " +
                "(SELECT \"Id\" FROM \"MediaServers\" WHERE \"ServiceType\" = 'Radarr');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"MissingMedia\" SET \"Tier\" = 'Series' WHERE \"Tier\" = 'Movie';");
            migrationBuilder.Sql("UPDATE \"SearchEvents\" SET \"Tier\" = 'Series' WHERE \"Tier\" = 'Movie';");
        }
    }
}
