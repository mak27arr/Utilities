namespace Utilities.Mobile.Infrastructure.Ocr;

using Utilities.Core.Infrastructure.Abstractions;

/// <summary>Legacy Tesseract recognizer retained only as an obsolete migration marker.</summary>
[Obsolete("Tesseract OCR has been removed from runtime use. The application uses ML Kit.")]
public sealed class TesseractReadingRecognizer : IReadingRecognizer
{
    public Task<ReadingRecognitionResult> RecognizeAsync(ReadOnlyMemory<byte> image,
        CancellationToken cancellationToken = default) =>
        Task.FromException<ReadingRecognitionResult>(
            new NotSupportedException("Tesseract OCR is obsolete and disabled. Use the registered ML Kit recognizer."));
}
