namespace Utilities.Mobile.Infrastructure.Persistence;

using System.Text.Json;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using Utilities.Mobile.Services;

public sealed class JsonAddressListCache(ILogger<JsonAddressListCache> logger) : IAddressListCache
{
    private readonly string _path = Path.Combine(FileSystem.AppDataDirectory, "home-addresses.json");
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private IReadOnlyList<CachedAddress>? _lastSnapshot;

    public Task<IReadOnlyList<CachedAddress>?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var totalTimer = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();
        var existsTimer = Stopwatch.StartNew();
        var exists = File.Exists(_path);
        StartupTiming.LogDuration("AddressCache.FileExists", existsTimer, $"exists: {exists}");
        if (!exists)
        {
            StartupTiming.LogDuration("AddressCache.ReadAsync total", totalTimer, "cache miss: file not found");
            return Task.FromResult<IReadOnlyList<CachedAddress>?>(null);
        }

        try
        {
            var readTimer = Stopwatch.StartNew();
            var json = File.ReadAllBytes(_path);
            StartupTiming.LogDuration("AddressCache.ReadFileBytes", readTimer, $"bytes: {json.Length}");

            cancellationToken.ThrowIfCancellationRequested();
            var deserializeTimer = Stopwatch.StartNew();
            var addresses = JsonSerializer.Deserialize(json, AddressCacheJsonContext.Default.CachedAddresses);
            StartupTiming.LogDuration("AddressCache.DeserializeJson", deserializeTimer, $"address count: {addresses?.Count ?? 0}");
            if (addresses is null)
            {
                StartupTiming.LogDuration("AddressCache.ReadAsync total", totalTimer, "cache miss: JSON value was null");
                return Task.FromResult<IReadOnlyList<CachedAddress>?>(null);
            }
            _lastSnapshot = addresses;
            StartupTiming.LogDuration("AddressCache.ReadAsync total", totalTimer, $"cache hit; address count: {addresses.Count}");
            return Task.FromResult<IReadOnlyList<CachedAddress>?>(addresses);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not read the home address cache; the address list will be loaded from the database.");
            StartupTiming.LogDuration("AddressCache.ReadAsync total", totalTimer, "cache miss: read failed");
            return Task.FromResult<IReadOnlyList<CachedAddress>?>(null);
        }
    }

    public async Task SaveAsync(IEnumerable<CachedAddress> addresses, CancellationToken cancellationToken = default)
    {
        var snapshot = addresses.ToArray();
        if (_lastSnapshot is not null && _lastSnapshot.SequenceEqual(snapshot)) return;
        _lastSnapshot = snapshot;
        await WriteAsync(snapshot, cancellationToken);
    }

    public void RememberSnapshot(IEnumerable<CachedAddress> addresses) => _lastSnapshot = addresses.ToArray();

    public async Task SaveLastSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (_lastSnapshot is not null)
            await WriteAsync(_lastSnapshot, cancellationToken);
    }

    private async Task WriteAsync(IReadOnlyList<CachedAddress> addresses, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        var temporaryPath = _path + ".tmp";
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(addresses.ToList(), AddressCacheJsonContext.Default.CachedAddresses);
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                await stream.WriteAsync(json, cancellationToken);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not write the home address cache.");
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(exception, "Could not remove the temporary home address cache file.");
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }
}

[JsonSerializable(typeof(List<CachedAddress>), TypeInfoPropertyName = "CachedAddresses")]
internal partial class AddressCacheJsonContext : JsonSerializerContext
{
}
