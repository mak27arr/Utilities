namespace Utilities.Mobile.Infrastructure.Diagnostics;

using System.Text;
using Microsoft.Extensions.Logging;

public sealed class LocalFileLoggerProvider : ILoggerProvider
{
    private const long MaxFileBytes = 1_000_000;
    private readonly object _gate = new();
    private long _writeCount;
    private long _writeElapsedTicks;
    private long _maxWriteElapsedTicks;

    public LocalFileLoggerProvider(string directory)
    {
        DirectoryPath = directory;
        ActiveLogPath = Path.Combine(directory, "utilities.log");
        PreviousLogPath = Path.Combine(directory, "utilities.previous.log");
    }

    public string DirectoryPath { get; }
    public string ActiveLogPath { get; }
    public string PreviousLogPath { get; }
    public ILogger CreateLogger(string categoryName) => new LocalFileLogger(this, categoryName);
    public void Dispose() { }

    public LoggerWriteMetrics CaptureWriteMetrics() => new(
        Interlocked.Read(ref _writeCount),
        Interlocked.Read(ref _writeElapsedTicks),
        Interlocked.Read(ref _maxWriteElapsedTicks));

    private void Write(string category, LogLevel level, EventId eventId, string message, Exception? exception)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var line = $"{DateTimeOffset.UtcNow:O} [{level}] {category} EventId={eventId.Id}: {message}" +
            (exception is null ? Environment.NewLine : $"{Environment.NewLine}{exception}{Environment.NewLine}");
        var bytes = Encoding.UTF8.GetBytes(line);
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                if (File.Exists(ActiveLogPath) && new FileInfo(ActiveLogPath).Length + bytes.Length > MaxFileBytes)
                {
                    if (File.Exists(PreviousLogPath)) File.Delete(PreviousLogPath);
                    File.Move(ActiveLogPath, PreviousLogPath);
                }
                File.AppendAllText(ActiveLogPath, line, Encoding.UTF8);
            }
            catch (Exception writeException)
            {
                System.Diagnostics.Trace.WriteLine($"Unable to write local diagnostics: {writeException}");
            }
            finally
            {
                var elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - started;
                Interlocked.Increment(ref _writeCount);
                Interlocked.Add(ref _writeElapsedTicks, elapsed);
                UpdateMaximum(elapsed);
            }
        }
    }

    private void UpdateMaximum(long elapsed)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _maxWriteElapsedTicks);
            if (elapsed <= current || Interlocked.CompareExchange(ref _maxWriteElapsedTicks, elapsed, current) == current)
                return;
        }
    }

    private sealed class LocalFileLogger(LocalFileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel)
        {
#if DEBUG
            return logLevel >= LogLevel.Information;
#else
            return category.StartsWith("Utilities.Reminders", StringComparison.Ordinal) ||
                category.StartsWith("Utilities.Mobile.Infrastructure.Reminders", StringComparison.Ordinal) ||
                category == "Utilities.Mobile.ViewModels.ReminderEditorViewModel"
                ? logLevel >= LogLevel.Information
                : logLevel >= LogLevel.Warning;
#endif
        }
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            owner.Write(category, logLevel, eventId, formatter(state, exception), exception);
        }
    }
}

public readonly record struct LoggerWriteMetrics(long Count, long ElapsedTicks, long MaxElapsedTicks)
{
    public double ElapsedMilliseconds => ElapsedTicks * 1000d / System.Diagnostics.Stopwatch.Frequency;
    public double MaxElapsedMilliseconds => MaxElapsedTicks * 1000d / System.Diagnostics.Stopwatch.Frequency;
}
