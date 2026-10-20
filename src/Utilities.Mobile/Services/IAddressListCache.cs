namespace Utilities.Mobile.Services;

public sealed record CachedAddress(Guid Id, string Name);

public interface IAddressListCache
{
    Task<IReadOnlyList<CachedAddress>?> ReadAsync(CancellationToken cancellationToken = default);
    void RememberSnapshot(IEnumerable<CachedAddress> addresses);
    Task SaveAsync(IEnumerable<CachedAddress> addresses, CancellationToken cancellationToken = default);
    Task SaveLastSnapshotAsync(CancellationToken cancellationToken = default);
}
