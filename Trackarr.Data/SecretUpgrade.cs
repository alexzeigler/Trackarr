using Microsoft.EntityFrameworkCore;

namespace Trackarr.Data;

/// <summary>
///     Runs at startup, after migrations. Encrypts API keys saved before encryption existed, and counts any that can't
///     be decrypted (the keys folder was lost or replaced) so the log can say so. Works on the raw column, below EF
///     Core's converter, so it sees what's actually stored.
/// </summary>
public static class SecretUpgrade
{
    public static async Task<(int Encrypted, int Unreadable)> ProtectExistingAsync(AppDbContext db,
        SecretProtector secrets, CancellationToken ct = default)
    {
        var rows = await db.Database
            .SqlQuery<StoredSecret>(
                $"""SELECT "Id", "ApiKey" AS "Value" FROM "MediaServers" WHERE "ApiKey" IS NOT NULL AND "ApiKey" <> ''""")
            .ToListAsync(ct);

        int encrypted = 0, unreadable = 0;
        foreach (var row in rows)
        {
            if (SecretProtector.IsProtected(row.Value))
            {
                if (!secrets.TryUnprotect(row.Value, out _)) unreadable++;
                continue;
            }

            var value = secrets.Protect(row.Value);
            await db.Database.ExecuteSqlAsync($"""UPDATE "MediaServers" SET "ApiKey" = {value} WHERE "Id" = {row.Id}""",
                ct);
            encrypted++;
        }

        if (encrypted > 0) await ScrubAsync(db, ct);
        return (encrypted, unreadable);
    }

    /// <summary>
    ///     SQLite leaves overwritten values in free pages and the WAL file until that space is reused, so a secret that
    ///     was just encrypted can still be read from the file in plain text. Rebuilding the file removes it. Only run
    ///     at startup, after replacing plain-text secrets: it rewrites the whole database.
    /// </summary>
    public static async Task ScrubAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("VACUUM", ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE)", ct);
    }

    private sealed record StoredSecret(int Id, string Value);
}
