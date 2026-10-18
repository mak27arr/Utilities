namespace Utilities.Mobile.Infrastructure.Media;

using System.Text;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using Utilities.Core.Infrastructure.Abstractions;

public sealed class MauiReadingHistoryFileTransfer : IReadingHistoryFileTransfer
{
    public async Task<PickedCsvFile?> PickCsvAsync(string pickerTitle, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = pickerTitle });
        cancellationToken.ThrowIfCancellationRequested();
        if (file is null) return null;
        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return new PickedCsvFile(file.FileName, await reader.ReadToEndAsync(cancellationToken));
    }

    public async Task ShareCsvAsync(string fileName, string shareTitle, string csv, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(FileSystem.CacheDirectory, fileName);
        await File.WriteAllTextAsync(path, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
        await Share.Default.RequestAsync(new ShareFileRequest(shareTitle, new ShareFile(path, "text/csv")));
    }
}
