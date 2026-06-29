using Trackarr.Core.Settings;
using Trackarr.Services.Settings;
using Trackarr.Services.UI;

namespace Trackarr.Tests;

public class BlacklistServiceTests
{
    private static BlacklistService Create(SqliteInMemoryFactory factory)
    {
        return new BlacklistService(factory, new DataChangeBus());
    }

    #region GetAllAsync

    [Fact]
    public async Task GetAll_EmptyDatabase_ReturnsEmptyList()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        var result = await service.GetAllAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAll_ReturnsEntriesOrderedByCreatedAtDescending()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        await service.AddAsync(new BlacklistEntry { Pattern = "first", Type = BlacklistType.Keyword });
        await service.AddAsync(new BlacklistEntry { Pattern = "second", Type = BlacklistType.Indexer });
        await service.AddAsync(new BlacklistEntry { Pattern = "third", Type = BlacklistType.ReleaseGroup });

        var result = await service.GetAllAsync();

        Assert.Equal(3, result.Count);
        Assert.True(result[0].CreatedAt >= result[1].CreatedAt);
        Assert.True(result[1].CreatedAt >= result[2].CreatedAt);
    }

    #endregion

    #region AddAsync

    [Fact]
    public async Task Add_PersistsEntryAndCanBeRetrieved()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        await service.AddAsync(new BlacklistEntry
        {
            Pattern = "bad-indexer", Type = BlacklistType.Indexer, IsRegex = false, Note = "Known bad"
        });

        var all = await service.GetAllAsync();
        Assert.Single(all);

        var entry = all[0];
        Assert.Equal("bad-indexer", entry.Pattern);
        Assert.Equal(BlacklistType.Indexer, entry.Type);
        Assert.False(entry.IsRegex);
        Assert.Equal("Known bad", entry.Note);
    }

    [Fact]
    public async Task Add_SetsCreatedAtToUtcNow()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        var before = DateTime.UtcNow;
        await service.AddAsync(new BlacklistEntry { Pattern = "test", Type = BlacklistType.Keyword });
        var after = DateTime.UtcNow;

        var all = await service.GetAllAsync();
        var entry = Assert.Single(all);

        Assert.InRange(entry.CreatedAt, before, after);
    }

    [Fact]
    public async Task Add_OverwritesClientSuppliedCreatedAt()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        var staleDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await service.AddAsync(new BlacklistEntry
        {
            Pattern = "test", Type = BlacklistType.Keyword, CreatedAt = staleDate
        });

        var entry = (await service.GetAllAsync())[0];
        Assert.NotEqual(staleDate, entry.CreatedAt);
        Assert.True(entry.CreatedAt > staleDate);
    }

    [Fact]
    public async Task Add_MultipleEntries_AllPersisted()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        await service.AddAsync(new BlacklistEntry { Pattern = "a", Type = BlacklistType.Keyword });
        await service.AddAsync(new BlacklistEntry { Pattern = "b", Type = BlacklistType.Indexer });
        await service.AddAsync(new BlacklistEntry { Pattern = "c", Type = BlacklistType.ReleaseGroup });

        var all = await service.GetAllAsync();
        Assert.Equal(3, all.Count);
    }

    #endregion

    #region GetByTypeAsync

    [Fact]
    public async Task GetByType_FiltersCorrectly()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        await service.AddAsync(new BlacklistEntry { Pattern = "idx1", Type = BlacklistType.Indexer });
        await service.AddAsync(new BlacklistEntry { Pattern = "kw1", Type = BlacklistType.Keyword });
        await service.AddAsync(new BlacklistEntry { Pattern = "idx2", Type = BlacklistType.Indexer });
        await service.AddAsync(new BlacklistEntry { Pattern = "rg1", Type = BlacklistType.ReleaseGroup });

        var indexers = await service.GetByTypeAsync(BlacklistType.Indexer);
        Assert.Equal(2, indexers.Count);
        Assert.All(indexers, e => Assert.Equal(BlacklistType.Indexer, e.Type));

        var keywords = await service.GetByTypeAsync(BlacklistType.Keyword);
        Assert.Single(keywords);
        Assert.Equal("kw1", keywords[0].Pattern);

        var releaseGroups = await service.GetByTypeAsync(BlacklistType.ReleaseGroup);
        Assert.Single(releaseGroups);
        Assert.Equal("rg1", releaseGroups[0].Pattern);
    }

    [Fact]
    public async Task GetByType_NoMatches_ReturnsEmptyList()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        await service.AddAsync(new BlacklistEntry { Pattern = "idx", Type = BlacklistType.Indexer });

        var result = await service.GetByTypeAsync(BlacklistType.ReleaseGroup);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByType_ReturnsOrderedByCreatedAtDescending()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        await service.AddAsync(new BlacklistEntry { Pattern = "older", Type = BlacklistType.Keyword });
        await service.AddAsync(new BlacklistEntry { Pattern = "newer", Type = BlacklistType.Keyword });

        var result = await service.GetByTypeAsync(BlacklistType.Keyword);

        Assert.Equal(2, result.Count);
        Assert.True(result[0].CreatedAt >= result[1].CreatedAt);
    }

    #endregion

    #region RemoveAsync

    [Fact]
    public async Task Remove_DeletesCorrectEntry()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        await service.AddAsync(new BlacklistEntry { Pattern = "keep", Type = BlacklistType.Keyword });
        await service.AddAsync(new BlacklistEntry { Pattern = "delete-me", Type = BlacklistType.Indexer });

        var all = await service.GetAllAsync();
        var toDelete = all.First(e => e.Pattern == "delete-me");

        await service.RemoveAsync(toDelete.Id);

        var remaining = await service.GetAllAsync();
        Assert.Single(remaining);
        Assert.Equal("keep", remaining[0].Pattern);
    }

    [Fact]
    public async Task Remove_NonExistentId_DoesNotThrow()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        await service.AddAsync(new BlacklistEntry { Pattern = "safe", Type = BlacklistType.Keyword });

        await service.RemoveAsync(9999);

        var all = await service.GetAllAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task Remove_LeavesOtherEntriesIntact()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        await service.AddAsync(new BlacklistEntry { Pattern = "a", Type = BlacklistType.Keyword });
        await service.AddAsync(new BlacklistEntry { Pattern = "b", Type = BlacklistType.Indexer });
        await service.AddAsync(new BlacklistEntry { Pattern = "c", Type = BlacklistType.ReleaseGroup });

        var all = await service.GetAllAsync();
        var middle = all.First(e => e.Pattern == "b");

        await service.RemoveAsync(middle.Id);

        var remaining = await service.GetAllAsync();
        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, e => e.Pattern == "a");
        Assert.Contains(remaining, e => e.Pattern == "c");
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task Update_ModifiesTargetFields()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        await service.AddAsync(new BlacklistEntry
        {
            Pattern = "original", Type = BlacklistType.Keyword, IsRegex = false, Note = "old note"
        });

        var entry = (await service.GetAllAsync())[0];

        await service.UpdateAsync(new BlacklistEntry
        {
            Id = entry.Id,
            Pattern = "updated",
            Type = BlacklistType.Indexer,
            IsRegex = true,
            Note = "new note"
        });

        var updated = (await service.GetAllAsync())[0];
        Assert.Equal("updated", updated.Pattern);
        Assert.Equal(BlacklistType.Indexer, updated.Type);
        Assert.True(updated.IsRegex);
        Assert.Equal("new note", updated.Note);
    }

    [Fact]
    public async Task Update_DoesNotModifyCreatedAt()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        await service.AddAsync(new BlacklistEntry { Pattern = "test", Type = BlacklistType.Keyword });

        var original = (await service.GetAllAsync())[0];
        var originalCreatedAt = original.CreatedAt;

        await service.UpdateAsync(new BlacklistEntry
        {
            Id = original.Id,
            Pattern = "changed",
            Type = BlacklistType.ReleaseGroup,
            IsRegex = true,
            Note = "note"
        });

        var updated = (await service.GetAllAsync())[0];
        Assert.Equal(originalCreatedAt, updated.CreatedAt);
    }

    [Fact]
    public async Task Update_NonExistentId_DoesNotThrow()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        await service.AddAsync(new BlacklistEntry { Pattern = "safe", Type = BlacklistType.Keyword });

        await service.UpdateAsync(new BlacklistEntry
        {
            Id = 9999,
            Pattern = "ghost",
            Type = BlacklistType.Indexer,
            IsRegex = true,
            Note = "does not exist"
        });

        var all = await service.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("safe", all[0].Pattern);
    }

    [Fact]
    public async Task Update_OnlyAffectsTargetEntry()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        await service.AddAsync(
            new BlacklistEntry { Pattern = "unchanged", Type = BlacklistType.Keyword, Note = "keep" });
        await service.AddAsync(new BlacklistEntry { Pattern = "target", Type = BlacklistType.Indexer, Note = "old" });

        var all = await service.GetAllAsync();
        var target = all.First(e => e.Pattern == "target");

        await service.UpdateAsync(new BlacklistEntry
        {
            Id = target.Id,
            Pattern = "modified",
            Type = BlacklistType.ReleaseGroup,
            IsRegex = true,
            Note = "new"
        });

        var after = await service.GetAllAsync();
        var other = after.First(e => e.Pattern == "unchanged");
        Assert.Equal(BlacklistType.Keyword, other.Type);
        Assert.Equal("keep", other.Note);
        Assert.False(other.IsRegex);
    }

    [Fact]
    public async Task Update_CanSetNoteToNull()
    {
        using var factory = new SqliteInMemoryFactory();
        var service = Create(factory);

        await service.AddAsync(new BlacklistEntry
        {
            Pattern = "test", Type = BlacklistType.Keyword, Note = "has note"
        });

        var entry = (await service.GetAllAsync())[0];

        await service.UpdateAsync(new BlacklistEntry
        {
            Id = entry.Id,
            Pattern = entry.Pattern,
            Type = entry.Type,
            IsRegex = entry.IsRegex,
            Note = null
        });

        var updated = (await service.GetAllAsync())[0];
        Assert.Null(updated.Note);
    }

    #endregion
}
