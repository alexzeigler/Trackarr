using Microsoft.EntityFrameworkCore;
using Trackarr.Core.Settings;
using Trackarr.Data;

namespace Trackarr.Services.Settings;

public class BlacklistService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<BlacklistEntry>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Blacklist.AsNoTracking().OrderByDescending(b => b.CreatedAt).ToListAsync();
    }

    public async Task<List<BlacklistEntry>> GetByTypeAsync(BlacklistType type)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Blacklist.AsNoTracking().Where(b => b.Type == type).OrderByDescending(b => b.CreatedAt)
            .ToListAsync();
    }

    public async Task AddAsync(BlacklistEntry entry)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        entry.CreatedAt = DateTime.UtcNow;
        db.Blacklist.Add(entry);
        await db.SaveChangesAsync();
    }

    public async Task RemoveAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Blacklist.Where(b => b.Id == id).ExecuteDeleteAsync();
    }

    public async Task UpdateAsync(BlacklistEntry entry)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.Blacklist.FindAsync(entry.Id);
        if (existing is null) return;

        existing.Pattern = entry.Pattern;
        existing.Type = entry.Type;
        existing.IsRegex = entry.IsRegex;
        existing.Note = entry.Note;
        await db.SaveChangesAsync();
    }
}
