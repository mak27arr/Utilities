namespace Utilities.Mobile.Infrastructure.Diagnostics;

using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using Utilities.Mobile.Services;

public sealed class MauiAppDiagnosticsExport(LocalFileLoggerProvider logs) : IAppDiagnostics
{
    public async Task ExportAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var exportPath = Path.Combine(FileSystem.CacheDirectory, $"utilities-diagnostics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt");
        await using (var output = new StreamWriter(exportPath, append: false, new System.Text.UTF8Encoding(false)))
        {
            foreach (var path in new[] { logs.PreviousLogPath, logs.ActiveLogPath })
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(path)) continue;
                await output.WriteLineAsync($"--- {Path.GetFileName(path)} ---");
                using var input = new StreamReader(path);
                while (await input.ReadLineAsync(cancellationToken) is { } line)
                    await output.WriteLineAsync(line.AsMemory(), cancellationToken);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        await Share.Default.RequestAsync(new ShareFileRequest(AppText.Get("ExportDiagnostics"), new ShareFile(exportPath, "text/plain")));
    }
}
