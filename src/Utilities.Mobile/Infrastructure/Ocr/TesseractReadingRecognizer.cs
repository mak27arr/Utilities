namespace Utilities.Mobile.Infrastructure.Ocr;

using TesseractOcrMaui;
using TesseractOcrMaui.Enums;
using TesseractOcrMaui.Results;
using Utilities.Core.BusinessLogic;
using Utilities.Core.Infrastructure.Abstractions;

/// <summary>Runs numeric recognition locally with the model packaged in the application.</summary>
public sealed class TesseractReadingRecognizer(ITesseract tesseract) : IReadingRecognizer
{
    private const string AllowedCharacters = "0123456789.,";
    private readonly SemaphoreSlim _recognitionGate = new(1, 1);

    public async Task<ReadingRecognitionResult> RecognizeAsync(ReadOnlyMemory<byte> image, CancellationToken cancellationToken = default)
    {
        if (image.IsEmpty) throw new ArgumentException("An image is required for recognition.", nameof(image));
        await _recognitionGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            tesseract.EngineConfiguration = engine =>
            {
                engine.DefaultSegmentationMode = PageSegmentationMode.SingleLine;
                engine.SetCharacterWhitelist(AllowedCharacters);
            };

            var result = await tesseract.RecognizeTextAsync(image.ToArray());
            cancellationToken.ThrowIfCancellationRequested();
            if (result.NotSuccess())
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Message)
                    ? $"Local OCR failed ({result.Status})."
                    : result.Message);

            var recognizedText = result.RecognisedText?.Trim() ?? string.Empty;
            var confidence = result.Confidence is >= 0 and <= 1 ? (double?)result.Confidence : null;
            return new ReadingRecognitionResult(recognizedText, RecognizedReadingParser.TryParse(recognizedText, out var value) ? value : null, confidence);
        }
        finally
        {
            _recognitionGate.Release();
        }
    }
}
