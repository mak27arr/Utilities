namespace Utilities.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Utilities.Core.Application.Abstractions;
using Utilities.Core.BusinessLogic;
using Utilities.Core.Domain.Models;

public sealed class EfUtilitiesStore(UtilitiesDbContext db, ILogger<EfUtilitiesStore>? logger = null, ILoggerFactory? loggerFactory = null) : IUtilitiesStore
{
    private static readonly Func<UtilitiesDbContext, IAsyncEnumerable<Address>> CompiledAddressListQuery =
        EF.CompileAsyncQuery((UtilitiesDbContext context) =>
            context.Addresses.TagWith("StartupTiming:HomeAddressList"));

    private readonly ILogger<EfUtilitiesStore> _logger = logger ?? NullLogger<EfUtilitiesStore>.Instance;
    private readonly ILogger _timingLogger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("Utilities.StartupTiming");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Local database initialization started.");
        try
        {
            // Keep this as a compile-time marker: discovering EF migrations is expensive on
            // Android cold starts. Tests verify that the marker tracks the migration list.
            var schemaVersion = UtilitiesSchemaVersion.Current;
            var storedSchemaVersion = await ReadSchemaVersionAsync(cancellationToken);
            _logger.LogInformation("Database schema comparison: stored version {StoredSchemaVersion}, application version {SchemaVersion}.", storedSchemaVersion, schemaVersion);
            if (storedSchemaVersion == schemaVersion)
            {
                _logger.LogInformation("Local database schema version {SchemaVersion} is current; skipping migration checks.", schemaVersion);
                LogInitializationDuration("EfUtilitiesStore.InitializeAsync total", timer, "schema already current");
                return;
            }
            if (storedSchemaVersion > schemaVersion)
            {
                _logger.LogWarning("Database schema version {StoredSchemaVersion} is newer than this app version {SchemaVersion}; leaving it unchanged.", storedSchemaVersion, schemaVersion);
                LogInitializationDuration("EfUtilitiesStore.InitializeAsync total", timer, "database schema newer than app");
                return;
            }

            var pendingTimer = System.Diagnostics.Stopwatch.StartNew();
            var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            LogInitializationDuration("Database.GetPendingMigrationsAsync", pendingTimer, $"pending count: {pendingMigrations.Length}");
            if (pendingMigrations.Length > 0)
            {
                _logger.LogInformation("Applying {MigrationCount} pending database migrations.", pendingMigrations.Length);
                var migrateTimer = System.Diagnostics.Stopwatch.StartNew();
                await db.Database.MigrateAsync(cancellationToken);
                LogInitializationDuration("Database.MigrateAsync", migrateTimer, $"migration count: {pendingMigrations.Length}");

                var existingNamesTimer = System.Diagnostics.Stopwatch.StartNew();
                var existingNames = await db.ServiceTypes.IgnoreQueryFilters().Select(item => item.Name).ToListAsync(cancellationToken);
                LogInitializationDuration("Database.LoadExistingServiceTypeNames", existingNamesTimer, $"existing count: {existingNames.Count}");
                var defaultsTimer = System.Diagnostics.Stopwatch.StartNew();
                var missingDefaults = BuiltInServiceTypes.CreateDefaults()
                    .Where(item => !existingNames.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                LogInitializationDuration("Database.FindMissingBuiltInServiceTypes", defaultsTimer, $"missing count: {missingDefaults.Length}");
                if (missingDefaults.Length > 0)
                {
                    _logger.LogInformation("Adding {ServiceTypeCount} missing built-in service types.", missingDefaults.Length);
                    db.ServiceTypes.AddRange(missingDefaults);
                    var saveDefaultsTimer = System.Diagnostics.Stopwatch.StartNew();
                    await db.SaveChangesAsync(cancellationToken);
                    LogInitializationDuration("Database.SaveMissingBuiltInServiceTypes", saveDefaultsTimer);
                }
                var loadBuiltInsTimer = System.Diagnostics.Stopwatch.StartNew();
                var builtIns = await db.ServiceTypes.IgnoreQueryFilters().Where(item => item.IsBuiltIn).ToListAsync(cancellationToken);
                LogInitializationDuration("Database.LoadBuiltInServiceTypes", loadBuiltInsTimer, $"built-in count: {builtIns.Count}");
                var refreshBuiltInsTimer = System.Diagnostics.Stopwatch.StartNew();
                var definitions = BuiltInServiceTypes.CreateDefaults().ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
                foreach (var existing in builtIns)
                    if (definitions.TryGetValue(existing.Name, out var definition)) existing.RefreshBuiltInDefinition(definition);
                LogInitializationDuration("Database.ApplyBuiltInServiceTypeDefinitions", refreshBuiltInsTimer, $"built-in count: {builtIns.Count}");
                if (db.ChangeTracker.HasChanges())
                {
                    var refreshSaveTimer = System.Diagnostics.Stopwatch.StartNew();
                    await db.SaveChangesAsync(cancellationToken);
                    LogInitializationDuration("Database.SaveRefreshedBuiltInServiceTypes", refreshSaveTimer);
                }
            }

            // Stamp only after migrations and any required initialization complete. If the app
            // stops before this write, the next launch safely rechecks EF's migration history.
            var writeVersionTimer = System.Diagnostics.Stopwatch.StartNew();
            await WriteSchemaVersionAsync(schemaVersion, cancellationToken);
            LogInitializationDuration("Database.WritePragmaUserVersion", writeVersionTimer, $"schema version: {schemaVersion}");
            LogInitializationDuration("EfUtilitiesStore.InitializeAsync total", timer, "schema initialization path completed");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Local database initialization failed after {ElapsedMilliseconds} ms.", timer.ElapsedMilliseconds);
            throw;
        }
    }

    private async Task<int> ReadSchemaVersionAsync(CancellationToken cancellationToken)
    {
        var totalTimer = System.Diagnostics.Stopwatch.StartNew();
        var openTimer = System.Diagnostics.Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(cancellationToken);
        LogInitializationDuration("Database.OpenConnection for PRAGMA user_version", openTimer);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            var queryTimer = System.Diagnostics.Stopwatch.StartNew();
            var value = await command.ExecuteScalarAsync(cancellationToken);
            LogInitializationDuration("Database.Execute PRAGMA user_version", queryTimer);
            return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
            LogInitializationDuration("Database.ReadSchemaVersionAsync total", totalTimer);
        }
    }

    private void LogInitializationDuration(string stage, System.Diagnostics.Stopwatch timer, string? detail = null)
    {
        var elapsed = timer.ElapsedMilliseconds;
        _timingLogger.LogInformation("Startup timing: {Stage} took {ElapsedMilliseconds} ms{Detail}.",
            stage, elapsed, string.IsNullOrWhiteSpace(detail) ? string.Empty : $"; {detail}");
    }

    private async Task WriteSchemaVersionAsync(int schemaVersion, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"PRAGMA user_version = {schemaVersion};";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public async Task<IReadOnlyList<T>> ListAsync<T>(Guid? parentId = null, bool includeDeleted = false, CancellationToken cancellationToken = default) where T : SoftDeletableEntity
    {
        var totalTimer = System.Diagnostics.Stopwatch.StartNew();
        var buildQueryTimer = System.Diagnostics.Stopwatch.StartNew();
        IQueryable<T> query = includeDeleted ? db.Set<T>().IgnoreQueryFilters() : db.Set<T>();
        if (parentId.HasValue)
        {
            var parent = parentId.Value;
            if (typeof(T) == typeof(Meter)) query = (IQueryable<T>)(object)((IQueryable<Meter>)query).Where(item => item.AddressId == parent);
            else if (typeof(T) == typeof(Subscription)) query = (IQueryable<T>)(object)((IQueryable<Subscription>)query).Where(item => item.AddressId == parent);
            else if (typeof(T) == typeof(Reading)) query = (IQueryable<T>)(object)((IQueryable<Reading>)query).Where(item => item.MeterId == parent);
            else if (typeof(T) == typeof(MeterTariffPeriod)) query = (IQueryable<T>)(object)((IQueryable<MeterTariffPeriod>)query).Where(item => item.MeterId == parent);
            else if (typeof(T) == typeof(SubscriptionRate)) query = (IQueryable<T>)(object)((IQueryable<SubscriptionRate>)query).Where(item => item.SubscriptionId == parent);
            else if (typeof(T) == typeof(ReminderSettings)) query = (IQueryable<T>)(object)((IQueryable<ReminderSettings>)query).Where(item => item.OwnerId == parent);
        }

        var isHomeAddressQuery = typeof(T) == typeof(Address) && parentId is null && !includeDeleted;
        if (isHomeAddressQuery)
        {
            LogInitializationDuration("Database.ListAddresses.BuildQuery", buildQueryTimer);

            var openTimer = System.Diagnostics.Stopwatch.StartNew();
            await db.Database.OpenConnectionAsync(cancellationToken);
            LogInitializationDuration("Database.ListAddresses.OpenConnection", openTimer);
            try
            {
                var enumerationTimer = System.Diagnostics.Stopwatch.StartNew();
                var result = new List<Address>();
                var firstRowTimer = System.Diagnostics.Stopwatch.StartNew();
                await using var enumerator = CompiledAddressListQuery(db).GetAsyncEnumerator(cancellationToken);
                var hasFirstRow = await enumerator.MoveNextAsync();
                LogInitializationDuration("Database.ListAddresses.CompiledQuery first MoveNext (query startup, command, first row)", firstRowTimer,
                    hasFirstRow ? "first row returned" : "no rows returned");
                if (hasFirstRow)
                {
                    result.Add(enumerator.Current);
                    var remainingRowsTimer = System.Diagnostics.Stopwatch.StartNew();
                    var remainingRowCount = 0;
                    while (await enumerator.MoveNextAsync())
                    {
                        result.Add(enumerator.Current);
                        remainingRowCount++;
                    }
                    LogInitializationDuration("Database.ListAddresses.CompiledQuery remaining MoveNext (remaining rows/materialization)", remainingRowsTimer,
                        $"additional rows: {remainingRowCount}");
                }
                LogInitializationDuration("Database.ListAddresses.CompiledQuery enumeration total", enumerationTimer,
                    $"returned count: {result.Count}");
                return (IReadOnlyList<T>)(object)result;
            }
            finally
            {
                var closeTimer = System.Diagnostics.Stopwatch.StartNew();
                await db.Database.CloseConnectionAsync();
                LogInitializationDuration("Database.ListAddresses.CloseConnection", closeTimer);
                LogInitializationDuration("Database.ListAddresses total", totalTimer);
            }
        }

        return await query.ToListAsync(cancellationToken);
    }

    public Task<Reading?> GetLatestReadingAsync(Guid meterId, CancellationToken cancellationToken = default) =>
        db.Readings.AsNoTracking().Where(item => item.MeterId == meterId)
            .OrderByDescending(item => item.MeasuredAt).ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<MeterTariffPeriod?> GetLatestTariffAsync(Guid meterId, CancellationToken cancellationToken = default) =>
        db.MeterTariffPeriods.AsNoTracking().Where(item => item.MeterId == meterId)
            .OrderByDescending(item => item.EffectiveFrom).ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> HasAnyReadingAsync(Guid meterId, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var query = includeDeleted ? db.Readings.IgnoreQueryFilters() : db.Readings.AsQueryable();
        return query.AnyAsync(item => item.MeterId == meterId, cancellationToken);
    }

    public async Task<T?> FindAsync<T>(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default) where T : SoftDeletableEntity
    {
        var query = includeDeleted ? db.Set<T>().IgnoreQueryFilters() : db.Set<T>();
        return await query.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public Task AddAsync(SoftDeletableEntity entity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        db.Add(entity);
        return Task.CompletedTask;
    }

    public async Task AddBatchAtomicallyAsync(IReadOnlyCollection<SoftDeletableEntity> entities, CancellationToken cancellationToken = default)
    {
        if (entities.Count == 0) return;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            db.AddRange(entities);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            catch (Exception rollbackException) { _logger.LogError(rollbackException, "CSV import transaction rollback failed."); }
            foreach (var entity in entities) db.Entry(entity).State = EntityState.Detached;
            _logger.LogError(exception, "Atomic reading import failed; {ReadingCount} unsaved readings were discarded.", entities.Count);
            throw;
        }
        finally { db.ChangeTracker.Clear(); }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
        db.ChangeTracker.Clear();
    }

    public async Task DeleteTreeAsync(SoftDeletableEntity root, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var rootType = root.GetType().Name;

        if (root is Address address)
        {
            var meters = await db.Meters.IgnoreQueryFilters().Where(item => item.AddressId == address.Id).ToListAsync(cancellationToken);
            var subscriptions = await db.Subscriptions.IgnoreQueryFilters().Where(item => item.AddressId == address.Id).ToListAsync(cancellationToken);
            var meterIds = meters.Select(item => item.Id).ToArray();
            var subscriptionIds = subscriptions.Select(item => item.Id).ToArray();
            var children = await db.Readings.IgnoreQueryFilters().Where(item => meterIds.Contains(item.MeterId)).Cast<SoftDeletableEntity>().ToListAsync(cancellationToken);
            children.AddRange(await db.MeterTariffPeriods.IgnoreQueryFilters().Where(item => meterIds.Contains(item.MeterId)).Cast<SoftDeletableEntity>().ToListAsync(cancellationToken));
            children.AddRange(await db.SubscriptionRates.IgnoreQueryFilters().Where(item => subscriptionIds.Contains(item.SubscriptionId)).Cast<SoftDeletableEntity>().ToListAsync(cancellationToken));
            var ownerIds = meterIds.Concat(subscriptionIds).ToArray();
            children.AddRange(await db.ReminderSettings.IgnoreQueryFilters().Where(item => ownerIds.Contains(item.OwnerId)).Cast<SoftDeletableEntity>().ToListAsync(cancellationToken));
            SoftDeleteTreeRules.SoftDelete(address, meters.Cast<SoftDeletableEntity>().Concat(subscriptions).Concat(children), at);
        }
        else if (root is Meter meter)
        {
            var children = await MeterChildren(meter.Id, cancellationToken);
            SoftDeleteTreeRules.SoftDelete(meter, children, at);
        }
        else if (root is Subscription subscription)
        {
            var children = await db.SubscriptionRates.IgnoreQueryFilters().Where(item => item.SubscriptionId == subscription.Id).Cast<SoftDeletableEntity>().ToListAsync(cancellationToken);
            children.AddRange(await db.ReminderSettings.IgnoreQueryFilters().Where(item => item.OwnerType == ReminderOwnerType.Subscription && item.OwnerId == subscription.Id).Cast<SoftDeletableEntity>().ToListAsync(cancellationToken));
            SoftDeleteTreeRules.SoftDelete(subscription, children, at);
        }
        else root.MarkDeleted(at, root.Id, rootType);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { db.ChangeTracker.Clear(); }
    }

    public async Task RestoreTreeAsync(string entityType, Guid id, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            SoftDeletableEntity root = entityType switch
            {
                nameof(Address) => await FindRequired<Address>(id, cancellationToken),
                nameof(Meter) => await FindRequired<Meter>(id, cancellationToken),
                nameof(Subscription) => await FindRequired<Subscription>(id, cancellationToken),
                nameof(Reading) => await FindRequired<Reading>(id, cancellationToken),
                nameof(ServiceType) => await FindRequired<ServiceType>(id, cancellationToken),
                nameof(MeterTariffPeriod) => await FindRequired<MeterTariffPeriod>(id, cancellationToken),
                nameof(SubscriptionRate) => await FindRequired<SubscriptionRate>(id, cancellationToken),
                nameof(ReminderSettings) => await FindRequired<ReminderSettings>(id, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(entityType))
            };

            if (root is Meter meterRoot && await db.Addresses.AnyAsync(item => item.Id == meterRoot.AddressId, cancellationToken) is false)
                throw new InvalidOperationException("Restore the meter's address before restoring the meter.");
            if (root is Subscription subscriptionRoot && await db.Addresses.AnyAsync(item => item.Id == subscriptionRoot.AddressId, cancellationToken) is false)
                throw new InvalidOperationException("Restore the subscription's address before restoring the subscription.");
            if (root is Reading readingRoot && await db.Meters.AnyAsync(item => item.Id == readingRoot.MeterId, cancellationToken) is false)
                throw new InvalidOperationException("Restore the meter before restoring its reading.");
            if (root is MeterTariffPeriod tariffRoot && await db.Meters.AnyAsync(item => item.Id == tariffRoot.MeterId, cancellationToken) is false)
                throw new InvalidOperationException("Restore the meter before restoring its tariff.");
            if (root is SubscriptionRate rateRoot && await db.Subscriptions.AnyAsync(item => item.Id == rateRoot.SubscriptionId, cancellationToken) is false)
                throw new InvalidOperationException("Restore the subscription before restoring its rate.");
            if (root is ReminderSettings reminderRoot)
            {
                var ownerExists = reminderRoot.OwnerType == ReminderOwnerType.Meter
                    ? await db.Meters.AnyAsync(item => item.Id == reminderRoot.OwnerId, cancellationToken)
                    : await db.Subscriptions.AnyAsync(item => item.Id == reminderRoot.OwnerId, cancellationToken);
                if (!ownerExists) throw new InvalidOperationException("Restore the reminder's owner before restoring the reminder.");
            }

            var descendants = root switch
            {
                Address address => await AddressChildren(address.Id, cancellationToken),
                Meter meter => await MeterChildren(meter.Id, cancellationToken),
                Subscription subscription => await SubscriptionChildren(subscription.Id, cancellationToken),
                _ => []
            };
            SoftDeleteTreeRules.Restore(root, descendants, at);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { db.ChangeTracker.Clear(); }
    }

    public async Task<IReadOnlyList<RecentlyDeletedItem>> GetRecentlyDeletedAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<RecentlyDeletedItem>();
        await AddDeleted<Address>(result, item => item.Name, cancellationToken);
        await AddDeleted<Meter>(result, item => item.Name, cancellationToken);
        await AddDeleted<Subscription>(result, item => item.Name, cancellationToken);
        await AddDeleted<ServiceType>(result, item => item.Name, cancellationToken);
        await AddDeleted<Reading>(result, item => $"Reading {item.ConfirmedValue}", cancellationToken);
        await AddDeleted<MeterTariffPeriod>(result, item => $"Tariff {item.RatePerUnit}", cancellationToken);
        await AddDeleted<SubscriptionRate>(result, item => $"Monthly fee {item.MonthlyFee}", cancellationToken);
        await AddDeleted<ReminderSettings>(result, item => $"{item.OwnerType} reminder", cancellationToken);
        return result.OrderByDescending(item => item.DeletedAt).ToArray();
    }

    public async Task<IReadOnlyList<ReminderSettings>> GetEnabledRemindersAsync(CancellationToken cancellationToken = default) =>
        await db.ReminderSettings.Where(item => item.IsEnabled).ToListAsync(cancellationToken);

    public async Task ClearAllDataAsync(CancellationToken cancellationToken = default)
    {
        try
        {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.RemoveRange(await db.ReminderSettings.IgnoreQueryFilters().ToListAsync(cancellationToken));
        db.RemoveRange(await db.Readings.IgnoreQueryFilters().ToListAsync(cancellationToken));
        db.RemoveRange(await db.MeterTariffPeriods.IgnoreQueryFilters().ToListAsync(cancellationToken));
        db.RemoveRange(await db.SubscriptionRates.IgnoreQueryFilters().ToListAsync(cancellationToken));
        db.RemoveRange(await db.Meters.IgnoreQueryFilters().ToListAsync(cancellationToken));
        db.RemoveRange(await db.Subscriptions.IgnoreQueryFilters().ToListAsync(cancellationToken));
        db.RemoveRange(await db.Addresses.IgnoreQueryFilters().ToListAsync(cancellationToken));
        db.RemoveRange(await db.ServiceTypes.IgnoreQueryFilters().Where(item => !item.IsBuiltIn).ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        }
        finally { db.ChangeTracker.Clear(); }
    }

    private async Task<List<SoftDeletableEntity>> AddressChildren(Guid addressId, CancellationToken token)
    {
        var meters = await db.Meters.IgnoreQueryFilters().Where(item => item.AddressId == addressId).ToListAsync(token);
        var subscriptions = await db.Subscriptions.IgnoreQueryFilters().Where(item => item.AddressId == addressId).ToListAsync(token);
        var meterIds = meters.Select(item => item.Id).ToArray();
        var subscriptionIds = subscriptions.Select(item => item.Id).ToArray();
        var all = meters.Cast<SoftDeletableEntity>().Concat(subscriptions).ToList();
        all.AddRange(await db.Readings.IgnoreQueryFilters().Where(item => meterIds.Contains(item.MeterId)).Cast<SoftDeletableEntity>().ToListAsync(token));
        all.AddRange(await db.MeterTariffPeriods.IgnoreQueryFilters().Where(item => meterIds.Contains(item.MeterId)).Cast<SoftDeletableEntity>().ToListAsync(token));
        all.AddRange(await db.SubscriptionRates.IgnoreQueryFilters().Where(item => subscriptionIds.Contains(item.SubscriptionId)).Cast<SoftDeletableEntity>().ToListAsync(token));
        var ownerIds = meterIds.Concat(subscriptionIds).ToArray();
        all.AddRange(await db.ReminderSettings.IgnoreQueryFilters().Where(item => ownerIds.Contains(item.OwnerId)).Cast<SoftDeletableEntity>().ToListAsync(token));
        return all;
    }

    private async Task<List<SoftDeletableEntity>> MeterChildren(Guid meterId, CancellationToken token)
    {
        var children = await db.Readings.IgnoreQueryFilters().Where(item => item.MeterId == meterId).Cast<SoftDeletableEntity>().ToListAsync(token);
        children.AddRange(await db.MeterTariffPeriods.IgnoreQueryFilters().Where(item => item.MeterId == meterId).Cast<SoftDeletableEntity>().ToListAsync(token));
        children.AddRange(await db.ReminderSettings.IgnoreQueryFilters().Where(item => item.OwnerType == ReminderOwnerType.Meter && item.OwnerId == meterId).Cast<SoftDeletableEntity>().ToListAsync(token));
        return children;
    }

    private async Task<List<SoftDeletableEntity>> SubscriptionChildren(Guid subscriptionId, CancellationToken token)
    {
        var children = await db.SubscriptionRates.IgnoreQueryFilters().Where(item => item.SubscriptionId == subscriptionId).Cast<SoftDeletableEntity>().ToListAsync(token);
        children.AddRange(await db.ReminderSettings.IgnoreQueryFilters().Where(item => item.OwnerType == ReminderOwnerType.Subscription && item.OwnerId == subscriptionId).Cast<SoftDeletableEntity>().ToListAsync(token));
        return children;
    }

    private async Task<T> FindRequired<T>(Guid id, CancellationToken token) where T : SoftDeletableEntity =>
        await db.Set<T>().IgnoreQueryFilters().SingleAsync(item => item.Id == id, token);

    private async Task AddDeleted<T>(List<RecentlyDeletedItem> target, Func<T, string> displayName, CancellationToken token) where T : SoftDeletableEntity
    {
        var roots = await db.Set<T>().IgnoreQueryFilters()
            .Where(item => item.DeletedAt != null && item.DeletedById == item.Id)
            .ToListAsync(token);
        target.AddRange(roots.Select(item => new RecentlyDeletedItem(typeof(T).Name, item.Id, displayName(item), item.DeletedAt!.Value)));
    }
}
