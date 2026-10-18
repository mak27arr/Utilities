namespace Utilities.Core.BusinessLogic;

/// <summary>Serializes a user-triggered asynchronous action and exposes its running state to UI commands.</summary>
public sealed class AsyncOperationGate
{
    private int _isRunning;
    public bool IsRunning => Volatile.Read(ref _isRunning) != 0;

    public async Task<bool> TryRunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0) return false;
        try
        {
            await operation(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { Volatile.Write(ref _isRunning, 0); }
    }
}
