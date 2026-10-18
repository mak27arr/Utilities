namespace Utilities.Infrastructure.Persistence;

using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

/// <summary>Times execution of the tagged home-address SQL command, separately from EF enumeration.</summary>
public sealed class HomeAddressCommandTimingInterceptor(ILoggerFactory loggerFactory) : DbCommandInterceptor
{
    private const string HomeAddressQueryTag = "StartupTiming:HomeAddressList";
    private readonly ConditionalWeakTable<DbCommand, Stopwatch> _timers = new();
    private readonly ILogger _logger = loggerFactory.CreateLogger("Utilities.StartupTiming");

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        StartTimer(command);
        return result;
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        LogElapsed(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        StartTimer(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        LogElapsed(command);
        return ValueTask.FromResult(result);
    }

    private void StartTimer(DbCommand command)
    {
        if (!command.CommandText.Contains(HomeAddressQueryTag, StringComparison.Ordinal)) return;
        _timers.Remove(command);
        _timers.Add(command, Stopwatch.StartNew());
    }

    private void LogElapsed(DbCommand command)
    {
        if (!_timers.TryGetValue(command, out var timer)) return;
        _timers.Remove(command);
        _logger.LogInformation("Startup timing: Database.ListAddresses.CommandExecuteReader took {ElapsedMilliseconds} ms.", timer.ElapsedMilliseconds);
    }
}
