using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trackarr.Data.Migrations
{
    /// <summary>
    ///     One-time reset of download health history. Before the C3 fix, attempts on two servers of the same
    ///     type could be linked to the other server's movie/series/episode (remote IDs restart at 1 on every
    ///     instance). Those rows don't store the remote media ID, so they can't be repaired; clear them instead.
    ///     MediaHealthSummaries is also rebuilt by the missing-media routine from SearchEvents on its next run.
    ///     Missing-media records, search events, the queue and server history are kept.
    /// </summary>
    public partial class ResetHealthHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Children before parents so this works with foreign keys enforced.
            migrationBuilder.Sql("DELETE FROM \"HealthEvents\";");
            migrationBuilder.Sql("DELETE FROM \"HealthSnapshots\";");
            migrationBuilder.Sql("DELETE FROM \"DownloadAttempts\";");
            migrationBuilder.Sql("DELETE FROM \"MediaHealthSummaries\";");
            migrationBuilder.Sql("DELETE FROM \"SourceHealthSummaries\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deleted history can't be restored. Rolling back is a no-op.
        }
    }
}
