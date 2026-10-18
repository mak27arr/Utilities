namespace Utilities.Core.Infrastructure.Abstractions;

public sealed record ReadingRecognitionResult(string RecognizedText, double? SuggestedValue, double? Confidence);

public interface IReadingRecognizer
{
    Task<ReadingRecognitionResult> RecognizeAsync(ReadOnlyMemory<byte> image, CancellationToken cancellationToken = default);
}
