namespace Utilities.Mobile.Infrastructure.Ocr;

using Android.Graphics;
using Android.Gms.Extensions;
using Utilities.Core.BusinessLogic;
using Utilities.Core.Domain.ValueObjects;
using Utilities.Mobile.Services;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Text;
using Xamarin.Google.MLKit.Vision.Text.Latin;
using MlKitText = Xamarin.Google.MLKit.Vision.Text.Text;

/// <summary>
/// Suggests a crop around the largest numeric text line found by the bundled on-device ML Kit OCR.
/// This is intentionally a suggestion: OCR may mistake a serial number for the reading, and the user can adjust it.
/// </summary>
public sealed class MlKitReadingAreaSuggester : IReadingAreaSuggester, IDisposable
{
    private const double MinimumUsefulRotationDegrees = 0.75;
    private const double MaximumAutomaticRotationDegrees = 90;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ITextRecognizer? _recognizer;
    private bool _disposed;

    public async Task<double?> EstimateRotationCorrectionDegreesAsync(string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var bitmap = BitmapFactory.DecodeFile(imagePath)
                ?? throw new InvalidDataException("The meter photo could not be decoded for rotation detection.");
            using var inputImage = InputImage.FromBitmap(bitmap, 0);
            var recognizer = _recognizer ??= TextRecognition.GetClient(TextRecognizerOptions.DefaultOptions);
            using var result = await recognizer.Process(inputImage).AsAsync<MlKitText>();
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = result.TextBlocks
                .SelectMany(block => block.Lines)
                .Where(line => IsNumericReadingLine(line.Text))
                .Select(line => new
                {
                    Angle = (double)line.Angle,
                    Area = (double)(line.BoundingBox?.Width() ?? 0) * (line.BoundingBox?.Height() ?? 0),
                    Width = (double)(line.BoundingBox?.Width() ?? 0)
                })
                .Where(line => double.IsFinite(line.Angle) && line.Area > 0 && line.Width > 0)
                .OrderByDescending(line => line.Area)
                .ThenByDescending(line => line.Width)
                .FirstOrDefault();

            if (candidate is null) return null;

            // ML Kit reports the baseline angle clockwise; a negative bitmap rotation levels it.
            var correction = NormalizeAngle(-candidate.Angle);
            return Math.Abs(correction) < MinimumUsefulRotationDegrees ||
                   Math.Abs(correction) > MaximumAutomaticRotationDegrees
                ? null
                : correction;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NormalizedReadingArea?> SuggestAsync(string imagePath, double rotationDegrees,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var bitmap = BitmapFactory.DecodeFile(imagePath)
                ?? throw new InvalidDataException("The meter photo could not be decoded for area detection.");
            using var rotatedBitmap = Rotate(bitmap, rotationDegrees);
            using var inputImage = InputImage.FromBitmap(rotatedBitmap, 0);
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

            return ReadingAreaCandidateSelector.SelectLargest(candidates, rotatedBitmap.Width, rotatedBitmap.Height);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsNumericReadingLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var digits = 0;
        foreach (var character in text)
        {
            if (char.IsAsciiDigit(character))
            {
                digits++;
                continue;
            }

            if (!char.IsWhiteSpace(character) && character is not ('.' or ',' or ':' or '\'' or '’' or '-' or '_'))
                return false;
        }

        return digits >= 3;
    }

    private static double NormalizeAngle(double degrees)
    {
        var normalized = degrees % 360;
        if (normalized > 180) normalized -= 360;
        if (normalized < -180) normalized += 360;
        return normalized;
    }

    private static Bitmap Rotate(Bitmap bitmap, double degrees)
    {
        var normalized = degrees % 360;
        if (Math.Abs(normalized) < 0.01) return bitmap.Copy(bitmap.GetConfig() ?? Bitmap.Config.Argb8888, false);

        using var matrix = new Matrix();
        matrix.PostRotate((float)normalized);
        return Bitmap.CreateBitmap(bitmap, 0, 0, bitmap.Width, bitmap.Height, matrix, true)
            ?? throw new InvalidOperationException("The meter photo could not be rotated for area detection.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _recognizer?.Close();
        _recognizer?.Dispose();
        _gate.Dispose();
        _disposed = true;
    }
}
