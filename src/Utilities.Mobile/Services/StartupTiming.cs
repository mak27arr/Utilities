namespace Utilities.Mobile.Services;

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Utilities.Mobile.Infrastructure.Diagnostics;

/// <summary>Writes structured timings for startup stages so slow phases can be isolated from each other.</summary>
public static class StartupTiming
{
    public const string LogCategory = "Utilities.StartupTiming";
    private static Stopwatch? _processTimer;
    private static ILogger? _logger;
    private static LocalFileLoggerProvider? _fileLoggerProvider;

    public static void Configure(ILogger logger, LocalFileLoggerProvider? fileLoggerProvider = null)
    {
        Volatile.Write(ref _logger, logger);
        Volatile.Write(ref _fileLoggerProvider, fileLoggerProvider);
    }

    public static LoggerWriteMetrics CaptureFileLoggerMetrics() =>
        Volatile.Read(ref _fileLoggerProvider)?.CaptureWriteMetrics() ?? default;

    public static void StartProcessTimer() => Interlocked.CompareExchange(ref _processTimer, Stopwatch.StartNew(), null);

    public static long ProcessElapsedMilliseconds => Volatile.Read(ref _processTimer)?.ElapsedMilliseconds ?? 0;

    public static void LogDuration(string stage, Stopwatch timer, string? detail = null)
    {
        var elapsed = timer.ElapsedMilliseconds;
        var processElapsed = ProcessElapsedMilliseconds;
        Volatile.Read(ref _logger)?.LogInformation("Startup timing: {Stage} took {ElapsedMilliseconds} ms (process +{ProcessElapsedMilliseconds} ms){Detail}.",
            stage, elapsed, processElapsed, string.IsNullOrWhiteSpace(detail) ? string.Empty : $"; {detail}");
    }

    public static void LogMilestone(string milestone) =>
        Volatile.Read(ref _logger)?.LogInformation("Startup milestone: {Milestone} at process +{ProcessElapsedMilliseconds} ms.", milestone, ProcessElapsedMilliseconds);

    public static void LogMilestoneAt(string milestone, long processElapsedMilliseconds) =>
        Volatile.Read(ref _logger)?.LogInformation("Startup milestone: {Milestone} at process +{ProcessElapsedMilliseconds} ms.", milestone, processElapsedMilliseconds);

    public static void LogFileLoggerMetrics(string stage, LoggerWriteMetrics before, LoggerWriteMetrics after)
    {
        var count = after.Count - before.Count;
        var ticks = after.ElapsedTicks - before.ElapsedTicks;
        var elapsedMilliseconds = ticks * 1000d / Stopwatch.Frequency;
        var maxMilliseconds = Math.Max(0, after.MaxElapsedMilliseconds);
        Volatile.Read(ref _logger)?.LogInformation(
            "Startup timing: {Stage} local file logger wrote {WriteCount} entries; cumulative write time {WriteMilliseconds:F2} ms; process maximum single write {MaxWriteMilliseconds:F2} ms.",
            stage, count, elapsedMilliseconds, maxMilliseconds);
    }
}
