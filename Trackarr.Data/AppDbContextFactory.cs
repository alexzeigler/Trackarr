using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Trackarr.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext> {
    public AppDbContext CreateDbContext(string[] args) {
        string? solutionRoot = Directory.GetParent(Directory.GetCurrentDirectory())?.FullName;
        string dbPath = Path.Combine(solutionRoot ?? string.Empty, "Trackarr.Data", "trackarr.db");
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}").Options;
        return new AppDbContext(options);
    }
}