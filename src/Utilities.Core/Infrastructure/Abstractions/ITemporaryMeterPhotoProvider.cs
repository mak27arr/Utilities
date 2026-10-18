namespace Utilities.Core.Infrastructure.Abstractions;

using Utilities.Core.Domain.ValueObjects;

public sealed record TemporaryMeterPhoto(string FilePath, int PixelWidth, int PixelHeight);

public interface ITemporaryMeterPhotoProvider
{
    Task<TemporaryMeterPhoto?> CaptureAsync(CancellationToken cancellationToken = default);
    Task<TemporaryMeterPhoto?> PickAsync(CancellationToken cancellationToken = default);
    Task<TemporaryMeterPhoto> CropAsync(TemporaryMeterPhoto source, NormalizedReadingArea area, double rotationDegrees,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(TemporaryMeterPhoto photo, CancellationToken cancellationToken = default);
}
