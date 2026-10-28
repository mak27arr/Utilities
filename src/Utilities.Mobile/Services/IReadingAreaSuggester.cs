namespace Utilities.Mobile.Services;

using Utilities.Core.Domain.ValueObjects;

public interface IReadingAreaSuggester
{
    Task<double?> EstimateRotationCorrectionDegreesAsync(string imagePath,
        CancellationToken cancellationToken = default);

    Task<NormalizedReadingArea?> SuggestAsync(string imagePath, double rotationDegrees,
        CancellationToken cancellationToken = default);
}
