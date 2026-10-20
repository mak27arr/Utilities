namespace Utilities.Mobile.Services;

using Utilities.Core.Domain.ValueObjects;

public interface IReadingAreaSuggester
{
    Task<NormalizedReadingArea?> SuggestAsync(string imagePath, double rotationDegrees,
        CancellationToken cancellationToken = default);
}
