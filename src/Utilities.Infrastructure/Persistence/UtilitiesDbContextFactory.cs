namespace Utilities.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public sealed class UtilitiesDbContextFactory : IDesignTimeDbContextFactory<UtilitiesDbContext>
{
    public UtilitiesDbContext CreateDbContext(string[] args)
    {
        var databasePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "utilities-design-time.db");
        var options = new DbContextOptionsBuilder<UtilitiesDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        return new UtilitiesDbContext(options);
    }
}
