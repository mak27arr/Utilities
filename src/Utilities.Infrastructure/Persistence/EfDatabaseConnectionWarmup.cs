namespace Utilities.Infrastructure.Persistence;

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class EfDatabaseConnectionWarmup(UtilitiesDbContext db, ILogger<EfDatabaseConnectionWarmup> logger) : IDatabaseConnectionWarmup
{
    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        var openTimer = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        logger.LogInformation("Background SQLite OpenConnectionAsync took {ElapsedMilliseconds} ms.", openTimer.ElapsedMilliseconds);
        var closeTimer = Stopwatch.StartNew();
        await db.Database.CloseConnectionAsync();
        logger.LogInformation("Background SQLite CloseConnectionAsync took {ElapsedMilliseconds} ms.", closeTimer.ElapsedMilliseconds);
    }
}
