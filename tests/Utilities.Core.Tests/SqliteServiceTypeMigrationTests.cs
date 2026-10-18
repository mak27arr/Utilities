namespace Utilities.Core.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using System.Data.Common;
using Utilities.Core.Domain.Models;
using Utilities.Infrastructure.Persistence;
using Xunit;

public sealed class SqliteServiceTypeMigrationTests
{
    [Fact]
    public async Task RuntimeSchemaVersionMatchesTheMigrationAssembly()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<UtilitiesDbContext>().UseSqlite(connection).Options;
        await using var db = new UtilitiesDbContext(options);

        Assert.Equal(UtilitiesSchemaVersion.Current, db.Database.GetMigrations().Count());
    }

    [Fact]
    public async Task InitializeSkipsDatabaseWritesWhenThereAreNoPendingMigrations()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var historyInterceptor = new MigrationHistoryCommandInterceptor();
        var options = new DbContextOptionsBuilder<UtilitiesDbContext>().UseSqlite(connection).AddInterceptors(historyInterceptor).Options;
        await using var db = new UtilitiesDbContext(options);
        var store = new EfUtilitiesStore(db);
        var savedChanges = 0;

        await store.InitializeAsync();
        Assert.True(historyInterceptor.HistoryReadCount > 0);
        Assert.Equal(UtilitiesSchemaVersion.Current, await ReadSchemaVersionAsync(db));
        db.SavedChanges += (_, _) => savedChanges++;
        historyInterceptor.Reset();

        await store.InitializeAsync();

        Assert.Equal(0, savedChanges);
        Assert.Equal(0, historyInterceptor.HistoryReadCount);
    }

    [Fact]
    public async Task InitializeMigratesExistingServiceTypesAndAddsNewBuiltIns()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<UtilitiesDbContext>().UseSqlite(connection).Options;
        await using var db = new UtilitiesDbContext(options);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync("20261014195132_InitialCreate");
        var legacyId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ServiceTypes (Id, Name, IsBuiltIn, IsActive, CreatedAt, UpdatedAt, DeletedAt, DeletedById, DeletedByType)
            VALUES ({legacyId}, 'Water', 1, 1, 0, NULL, NULL, NULL, NULL)
            """);

        await new EfUtilitiesStore(db).InitializeAsync();

        var migratedWater = await db.ServiceTypes.SingleAsync(item => item.Id == legacyId);
        Assert.Equal(ServiceTypeCategory.Meter, migratedWater.Category);
        Assert.Equal("m³", migratedWater.DefaultUnit);
        Assert.Equal("service-water", migratedWater.IconKey);
        Assert.Equal(UtilitiesSchemaVersion.Current, await ReadSchemaVersionAsync(db));

        var builtIns = await db.ServiceTypes.ToListAsync();
        Assert.Contains(builtIns, item => item.Name == "Gas" && item.Category == ServiceTypeCategory.Meter);
        Assert.Equal("service-gas", builtIns.Single(item => item.Name == "Gas").IconKey);
        Assert.Contains(builtIns, item => item.Name == "Housing and utilities" && item.Category == ServiceTypeCategory.Subscription);
        Assert.Equal(BuiltInServiceTypes.CreateDefaults().Count, builtIns.Count);
    }

    private static async Task<int> ReadSchemaVersionAsync(UtilitiesDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private sealed class MigrationHistoryCommandInterceptor : DbCommandInterceptor
    {
        public int HistoryReadCount { get; private set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase))
                HistoryReadCount++;
            return ValueTask.FromResult(result);
        }

        public void Reset() => HistoryReadCount = 0;
    }
}
