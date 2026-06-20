using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Trackarr.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var solutionRoot = Directory.GetParent(Directory.GetCurrentDirectory())?.FullName;
        var dbPath = Path.Combine(solutionRoot ?? string.Empty, "Trackarr.Data", "trackarr.db");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={dbPath}").Options;
        return new AppDbContext(options);
    }
}