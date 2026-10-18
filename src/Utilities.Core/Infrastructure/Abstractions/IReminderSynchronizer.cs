namespace Utilities.Core.Infrastructure.Abstractions;

public interface IReminderSynchronizer
{
    Task SynchronizeAsync(CancellationToken cancellationToken = default);
}
