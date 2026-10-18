namespace Utilities.Core.Infrastructure.Abstractions;

public sealed record PickedCsvFile(string FileName, string Content);

public interface IReadingHistoryFileTransfer
{
    Task<PickedCsvFile?> PickCsvAsync(string pickerTitle, CancellationToken cancellationToken = default);
    Task ShareCsvAsync(string fileName, string shareTitle, string csv, CancellationToken cancellationToken = default);
}
