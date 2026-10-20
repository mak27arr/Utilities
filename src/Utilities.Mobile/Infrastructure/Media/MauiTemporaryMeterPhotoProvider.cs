namespace Utilities.Mobile.Infrastructure.Media;

using Android.Graphics;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using Utilities.Core.BusinessLogic;
using Utilities.Core.Domain.ValueObjects;
using Utilities.Core.Infrastructure.Abstractions;

public sealed class MauiTemporaryMeterPhotoProvider : ITemporaryMeterPhotoProvider
{
    public MauiTemporaryMeterPhotoProvider()
    {
        var directory = System.IO.Path.Combine(FileSystem.CacheDirectory, "meter-ocr-temp");
        if (Directory.Exists(directory))
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
    }

    public async Task<TemporaryMeterPhoto?> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions
        {
            RotateImage = true,
            PreserveMetaData = false,
            MaximumWidth = 2048,
            MaximumHeight = 2048,
            CompressionQuality = 90
        });
        if (result is null) return null;
        try { return await CopyToCacheAsync(result, cancellationToken); }
        finally { TryDelete(result.FullPath); }
    }

    public async Task<TemporaryMeterPhoto?> PickAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
        {
            RotateImage = true,
            PreserveMetaData = false,
            MaximumWidth = 2048,
            MaximumHeight = 2048,
            CompressionQuality = 90,
            SelectionLimit = 1
        });
        var result = results.FirstOrDefault();
        return result is null ? null : await CopyToCacheAsync(result, cancellationToken);
    }

    public async Task<TemporaryMeterPhoto> CropAsync(TemporaryMeterPhoto source, NormalizedReadingArea area,
        double rotationDegrees, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = BitmapFactory.DecodeFile(source.FilePath) ?? throw new InvalidDataException("The selected image could not be decoded.");
        Bitmap? rotatedBitmap = null;
        var imageToCrop = bitmap;
        if (Math.Abs(rotationDegrees) > 0.01)
        {
            using var matrix = new Matrix();
            matrix.SetRotate((float)rotationDegrees);
            rotatedBitmap = Bitmap.CreateBitmap(bitmap, 0, 0, bitmap.Width, bitmap.Height, matrix, true);
            imageToCrop = rotatedBitmap;
        }

        using (rotatedBitmap)
        {
            var bounds = ReadingAreaGeometry.GetPixelBounds(area, imageToCrop.Width, imageToCrop.Height);
            using var crop = Bitmap.CreateBitmap(imageToCrop, bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            using var prepared = PrepareForRecognition(crop);
            var path = CreateTemporaryPath("crop");
            try
            {
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                cancellationToken.ThrowIfCancellationRequested();
                var jpegFormat = Bitmap.CompressFormat.Jpeg ?? throw new InvalidOperationException("JPEG encoding is unavailable.");
                if (!prepared.Compress(jpegFormat, 98, output)) throw new IOException("The OCR image could not be written.");
                await output.FlushAsync(cancellationToken);
                return new TemporaryMeterPhoto(path, prepared.Width, prepared.Height);
            }
            catch
            {
                TryDelete(path);
                throw;
            }
        }
    }

    private static Bitmap PrepareForRecognition(Bitmap crop)
    {
        const int targetCharacterImageHeight = 160;
        const float maxScale = 4;
        var scale = Math.Clamp(targetCharacterImageHeight / (float)crop.Height, 1, maxScale);
        var scaledWidth = Math.Max(1, (int)Math.Round(crop.Width * scale));
        var scaledHeight = Math.Max(1, (int)Math.Round(crop.Height * scale));
        using var scaled = Bitmap.CreateScaledBitmap(crop, scaledWidth, scaledHeight, filter: true);
        var prepared = Bitmap.CreateBitmap(scaledWidth, scaledHeight, Bitmap.Config.Argb8888!)
            ?? throw new InvalidOperationException("Could not allocate the OCR image.");
        using var canvas = new Canvas(prepared);
        using var paint = new Paint(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
        // Convert the red and black meter wheels to one high-contrast grayscale image.
        const float contrast = 1.35f;
        const float offset = -45f;
        using var filter = new ColorMatrixColorFilter(new ColorMatrix(
        [
            0.299f * contrast, 0.587f * contrast, 0.114f * contrast, 0, offset,
            0.299f * contrast, 0.587f * contrast, 0.114f * contrast, 0, offset,
            0.299f * contrast, 0.587f * contrast, 0.114f * contrast, 0, offset,
            0, 0, 0, 1, 0
        ]));
        paint.SetColorFilter(filter);
        canvas.DrawBitmap(scaled, 0, 0, paint);
        return prepared;
    }

    public Task DeleteAsync(TemporaryMeterPhoto photo, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryDelete(photo.FilePath);
        return Task.CompletedTask;
    }

    private static async Task<TemporaryMeterPhoto> CopyToCacheAsync(FileResult result, CancellationToken cancellationToken)
    {
        var path = CreateTemporaryPath("photo");
        try
        {
            await using var input = await result.OpenReadAsync();
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
            using var options = new BitmapFactory.Options { InJustDecodeBounds = true };
            using var unused = BitmapFactory.DecodeFile(path, options);
            if (options.OutWidth <= 0 || options.OutHeight <= 0) throw new InvalidDataException("The selected file is not a readable image.");
            return new TemporaryMeterPhoto(path, options.OutWidth, options.OutHeight);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    private static string CreateTemporaryPath(string prefix)
    {
        var directory = System.IO.Path.Combine(FileSystem.CacheDirectory, "meter-ocr-temp");
        Directory.CreateDirectory(directory);
        return System.IO.Path.Combine(directory, $"{prefix}-{Guid.NewGuid():N}.jpg");
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}

