namespace Utilities.Mobile.Infrastructure.Ocr;

using Android.Graphics;
using Microsoft.Maui.Storage;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Utilities.Core.Domain.ValueObjects;
using Utilities.Mobile.Services;

/// <summary>Finds the reading window with the trained YOLO detector, falling back to the existing ML Kit suggestion.</summary>
public sealed class YoloReadingAreaSuggester(MlKitReadingAreaSuggester fallback) : IReadingAreaSuggester, IDisposable
{
    private const int InputSize = 640;
    private const float MinimumConfidence = 0.20f;
    private const int ReadingAreaClassId = 0;
    private const string ModelAssetName = "reading_area.onnx";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private InferenceSession? _session;
    private string? _inputName;
    private bool _disposed;

    public Task<double?> EstimateRotationCorrectionDegreesAsync(string imagePath,
        CancellationToken cancellationToken = default) =>
        fallback.EstimateRotationCorrectionDegreesAsync(imagePath, cancellationToken);

    public async Task<NormalizedReadingArea?> SuggestAsync(string imagePath, double rotationDegrees,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = _session ??= await CreateSessionAsync(cancellationToken);
            var detected = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var bitmap = BitmapFactory.DecodeFile(imagePath)
                    ?? throw new InvalidDataException("The meter photo could not be decoded for area detection.");
                using var rotated = Rotate(bitmap, rotationDegrees);
                return Detect(rotated, session, _inputName!, cancellationToken);
            }, cancellationToken);
            return detected ?? await fallback.SuggestAsync(imagePath, rotationDegrees, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static NormalizedReadingArea? Detect(Bitmap source, InferenceSession session, string inputName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var scale = Math.Min((double)InputSize / source.Width, (double)InputSize / source.Height);
        var resizedWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var resizedHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        var padX = (InputSize - resizedWidth) / 2;
        var padY = (InputSize - resizedHeight) / 2;

        using var inputBitmap = Bitmap.CreateBitmap(InputSize, InputSize, Bitmap.Config.Argb8888!)
            ?? throw new InvalidOperationException("Could not allocate the YOLO input image.");
        using (var canvas = new Canvas(inputBitmap))
        using (var paint = new Paint(PaintFlags.FilterBitmap | PaintFlags.AntiAlias))
        using (var sourceRect = new Rect(0, 0, source.Width, source.Height))
        using (var targetRect = new Rect(padX, padY, padX + resizedWidth, padY + resizedHeight))
        {
            canvas.DrawColor(Color.Rgb(114, 114, 114));
            canvas.DrawBitmap(source, sourceRect, targetRect, paint);
        }

        var pixels = new int[InputSize * InputSize];
        inputBitmap.GetPixels(pixels, 0, InputSize, 0, 0, InputSize, InputSize);
        var input = new float[3 * pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            var color = pixels[i];
            input[i] = ((color >> 16) & 0xFF) / 255f;
            input[pixels.Length + i] = ((color >> 8) & 0xFF) / 255f;
            input[2 * pixels.Length + i] = (color & 0xFF) / 255f;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var tensor = new DenseTensor<float>(input, [1, 3, InputSize, InputSize]);
        var modelInput = NamedOnnxValue.CreateFromTensor(inputName, tensor);
        using var outputs = session.Run([modelInput]);
        var predictions = outputs.First().AsTensor<float>();
        if (predictions.Rank != 3 || predictions.Dimensions[0] < 1 || predictions.Dimensions[2] < 6)
            throw new InvalidDataException("The YOLO model returned an unexpected output shape.");

        var bestConfidence = MinimumConfidence;
        var bestLeft = 0f;
        var bestTop = 0f;
        var bestRight = 0f;
        var bestBottom = 0f;
        var count = predictions.Dimensions[1];
        for (var i = 0; i < count; i++)
        {
            var confidence = predictions[0, i, 4];
            var classId = (int)MathF.Round(predictions[0, i, 5]);
            if (classId != ReadingAreaClassId || !float.IsFinite(confidence) || confidence <= bestConfidence)
                continue;

            bestConfidence = confidence;
            bestLeft = predictions[0, i, 0];
            bestTop = predictions[0, i, 1];
            bestRight = predictions[0, i, 2];
            bestBottom = predictions[0, i, 3];
        }

        if (bestConfidence <= MinimumConfidence) return null;

        var scaleX = (double)resizedWidth / source.Width;
        var scaleY = (double)resizedHeight / source.Height;
        var left = Math.Clamp((bestLeft - padX) / scaleX, 0, source.Width - 1d);
        var top = Math.Clamp((bestTop - padY) / scaleY, 0, source.Height - 1d);
        var right = Math.Clamp((bestRight - padX) / scaleX, left + 1, source.Width);
        var bottom = Math.Clamp((bestBottom - padY) / scaleY, top + 1, source.Height);
        return new NormalizedReadingArea(left / source.Width, top / source.Height,
            (right - left) / source.Width, (bottom - top) / source.Height);
    }

    private async Task<InferenceSession> CreateSessionAsync(CancellationToken cancellationToken)
    {
        var modelPath = System.IO.Path.Combine(FileSystem.CacheDirectory, ModelAssetName);
        if (!File.Exists(modelPath))
        {
            var temporaryPath = modelPath + ".tmp";
            try
            {
                await using var modelStream = await FileSystem.OpenAppPackageFileAsync(ModelAssetName);
                await using (var output = new System.IO.FileStream(temporaryPath, System.IO.FileMode.Create,
                                 System.IO.FileAccess.Write, System.IO.FileShare.None))
                    await modelStream.CopyToAsync(output, cancellationToken);
                File.Move(temporaryPath, modelPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var session = new InferenceSession(modelPath);
        _inputName = session.InputMetadata.Keys.First();
        return session;
    }

    private static Bitmap Rotate(Bitmap bitmap, double degrees)
    {
        var normalized = degrees % 360;
        if (Math.Abs(normalized) < 0.01)
            return bitmap.Copy(Bitmap.Config.Argb8888!, false)
                ?? throw new InvalidOperationException("The meter photo could not be copied for area detection.");

        using var matrix = new Matrix();
        matrix.PostRotate((float)normalized);
        return Bitmap.CreateBitmap(bitmap, 0, 0, bitmap.Width, bitmap.Height, matrix, true)
            ?? throw new InvalidOperationException("The meter photo could not be rotated for area detection.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _session?.Dispose();
        _gate.Dispose();
        _disposed = true;
    }
}
