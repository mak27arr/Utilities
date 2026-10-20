namespace Utilities.Infrastructure.Persistence;

public interface IDatabaseConnectionWarmup
{
    Task WarmupAsync(CancellationToken cancellationToken = default);
}
