namespace Utilities.Mobile.Infrastructure.Ocr;

using Android.Gms.Extensions;
using Android.Graphics;
using Utilities.Core.BusinessLogic;
using Utilities.Core.Infrastructure.Abstractions;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Text;
using Xamarin.Google.MLKit.Vision.Text.Latin;
using MlKitText = Xamarin.Google.MLKit.Vision.Text.Text;

/// <summary>Runs the bundled, on-device Latin ML Kit recognizer.</summary>
public sealed class MlKitReadingRecognizer : IReadingRecognizer, IDisposable
{
    private readonly SemaphoreSlim _recognitionGate = new(1, 1);
    private ITextRecognizer? _recognizer;
    private bool _disposed;

    public async Task<ReadingRecognitionResult> RecognizeAsync(ReadOnlyMemory<byte> image,
        CancellationToken cancellationToken = default)
    {
        if (image.IsEmpty) throw new ArgumentException("An image is required for recognition.", nameof(image));
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _recognitionGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = image.ToArray();
            using var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length)
                ?? throw new InvalidDataException("The OCR image could not be decoded.");
            using var inputImage = InputImage.FromBitmap(bitmap, 0);
            var recognizer = _recognizer ??= TextRecognition.GetClient(TextRecognizerOptions.DefaultOptions);
            using var result = await recognizer.Process(inputImage).AsAsync<MlKitText>();
            cancellationToken.ThrowIfCancellationRequested();

            var candidates = new List<NumericTextLineCandidate>();
            foreach (var block in result.TextBlocks)
            foreach (var line in block.Lines)
            {
                var bounds = line.BoundingBox;
                if (bounds is null) continue;
                candidates.Add(new NumericTextLineCandidate(line.Text ?? string.Empty,
                    bounds.Left, bounds.Top, bounds.Width(), bounds.Height()));
            }

            var hasNumericLine = RecognizedReadingParser.TrySelectLargestNumericLine(candidates,
                out var recognizedText, out var value);
            var suggestedValue = hasNumericLine ? value : (double?)null;
            return new ReadingRecognitionResult(recognizedText, suggestedValue, null);
        }
        finally
        {
            _recognitionGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _recognizer?.Close();
        _recognizer?.Dispose();
        _recognitionGate.Dispose();
        _disposed = true;
    }
}
