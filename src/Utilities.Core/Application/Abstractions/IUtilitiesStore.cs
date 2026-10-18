namespace Utilities.Core.Application.Abstractions;

using Utilities.Core.Domain.Models;

public sealed record RecentlyDeletedItem(string EntityType, Guid Id, string DisplayName, DateTimeOffset DeletedAt);

public interface IUtilitiesStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<T>> ListAsync<T>(Guid? parentId = null, bool includeDeleted = false, CancellationToken cancellationToken = default) where T : SoftDeletableEntity;
    Task<Reading?> GetLatestReadingAsync(Guid meterId, CancellationToken cancellationToken = default);
    Task<MeterTariffPeriod?> GetLatestTariffAsync(Guid meterId, CancellationToken cancellationToken = default);
    Task<bool> HasAnyReadingAsync(Guid meterId, bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task<T?> FindAsync<T>(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default) where T : SoftDeletableEntity;
    Task AddAsync(SoftDeletableEntity entity, CancellationToken cancellationToken = default);
    Task AddBatchAtomicallyAsync(IReadOnlyCollection<SoftDeletableEntity> entities, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    Task DeleteTreeAsync(SoftDeletableEntity root, DateTimeOffset at, CancellationToken cancellationToken = default);
    Task RestoreTreeAsync(string entityType, Guid id, DateTimeOffset at, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecentlyDeletedItem>> GetRecentlyDeletedAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReminderSettings>> GetEnabledRemindersAsync(CancellationToken cancellationToken = default);
    Task ClearAllDataAsync(CancellationToken cancellationToken = default);
}
