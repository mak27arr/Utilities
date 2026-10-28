namespace Utilities.Mobile.ViewModels;

using Microsoft.Maui.Controls;
using Utilities.Core.Application.UseCases;
using Utilities.Core.BusinessLogic;
using Utilities.Core.Domain.ValueObjects;
using Utilities.Core.Infrastructure.Abstractions;
using Utilities.Mobile.Services;

public sealed class PhotoCropViewModel : ViewModelBase
{
    private static readonly NormalizedReadingArea NoCandidateFallbackArea = new(0.15, 0.3, 0.7, 0.4);
    private readonly UtilitiesApplicationService _application;
    private readonly Guid _meterId;
    private readonly ITemporaryMeterPhotoProvider _photos;
    private readonly ICameraPermissionService _cameraPermission;
    private readonly IUserDialogs _dialogs;
    private readonly IAppNavigation _navigation;
    private readonly IReadingAreaSuggester _areaSuggester;
    private TemporaryMeterPhoto? _original;
    private TemporaryMeterPhoto? _cropped;
    private NormalizedReadingArea _area = new(0.25, 0.35, 0.5, 0.3);
    private ImageSource? _currentImage;
    private ReadingRecognitionResult? _recognition;
    private string _readingValue = string.Empty;
    private double _rotationDegrees;
    private bool _isWorking;
    private bool _initialized;

    public PhotoCropViewModel(UtilitiesApplicationService application, Guid meterId, ITemporaryMeterPhotoProvider photos,
        ICameraPermissionService cameraPermission, IUserDialogs dialogs, IAppNavigation navigation,
        IReadingAreaSuggester areaSuggester)
    {
        _application = application;
        _meterId = meterId;
        _photos = photos;
        _cameraPermission = cameraPermission;
        _dialogs = dialogs;
        _navigation = navigation;
        _areaSuggester = areaSuggester;
        ChoosePhotoCommand = new Command(async () => await LoadPhotoAsync(capture: true), () => !IsWorking);
        ChooseGalleryPhotoCommand = new Command(async () => await LoadPhotoAsync(capture: false), () => !IsWorking && !HasPhoto);
        RecognizeReadingCommand = new Command(async () => await RecognizeReadingAsync(), () => CanRecognize);
        SaveReadingCommand = new Command(async () => await SaveReadingAsync(), () => CanSaveReading);
        CancelCommand = new Command(async () => await CancelAsync(), () => !IsWorking);
        RotateLeftCommand = new Command(() => RotationDegrees -= 90);
        RotateRightCommand = new Command(() => RotationDegrees += 90);
        ResetRotationCommand = new Command(() => RotationDegrees = 0);
        FindReadingAreaCommand = new Command(async () => await FindReadingAreaAsync(showNoCandidate: true),
            () => HasPhoto && !IsWorking);
    }

    public Command ChoosePhotoCommand { get; }
    public Command ChooseGalleryPhotoCommand { get; }
    public Command RecognizeReadingCommand { get; }
    public Command SaveReadingCommand { get; }
    public Command CancelCommand { get; }
    public Command RotateLeftCommand { get; }
    public Command RotateRightCommand { get; }
    public Command ResetRotationCommand { get; }
    public Command FindReadingAreaCommand { get; }

    public NormalizedReadingArea Area => _area;
    public double RotationDegrees
    {
        get => _rotationDegrees;
        set
        {
            var normalized = NormalizeRotation(value);
            if (!SetProperty(ref _rotationDegrees, normalized)) return;
            OnPropertyChanged(nameof(RotationText));
        }
    }
    public string RotationText => $"{AppText.Get("Rotation")}: {RotationDegrees:0}°";
    public string RotateLeftText => AppText.Get("RotateLeft90");
    public string RotateRightText => AppText.Get("RotateRight90");
    public string ResetRotationText => AppText.Get("ResetRotation");
    public TemporaryMeterPhoto? CurrentPhoto => _original;
    public ImageSource? CurrentImage { get => _currentImage; private set => SetProperty(ref _currentImage, value); }
    public ReadingRecognitionResult? Recognition => _recognition;
    public string ReadingValue
    {
        get => _readingValue;
        set
        {
            if (!SetProperty(ref _readingValue, value)) return;
            OnPropertyChanged(nameof(CanSaveReading));
            SaveReadingCommand.ChangeCanExecute();
        }
    }
    public bool IsWorking { get => _isWorking; private set { if (SetProperty(ref _isWorking, value)) NotifyState(); } }
    public bool HasPhoto => _original is not null;
    public bool HasRecognition => _recognition is not null;
    public bool ShowPhotoChoices => !HasPhoto;
    public bool IsRecognitionVisible => HasRecognition;
    public bool IsRecognitionNotVisible => !HasRecognition;
    public bool CanRecognize => HasPhoto && !IsWorking;
    public bool CanSaveReading => HasRecognition && !IsWorking && !string.IsNullOrWhiteSpace(ReadingValue);
    public string CameraActionText => AppText.Get(HasPhoto ? "RetakePhoto" : "TakePhoto");
    public string InstructionsText => AppText.Get("ChoosePhotoInstructions");
    public string InstructionsTitle => AppText.Get("PhotoReadingTitle");
    public string ChooseGalleryPhotoText => AppText.Get("ChooseFromGallery");
    public string RecognizeText => AppText.Get("RecognizeReading");
    public string AutoSelectReadingAreaText => AppText.Get("AutoSelectReadingArea");
    public string SaveReadingText => AppText.Get("Save");
    public string CancelText => AppText.Get("Cancel");
    public string WorkingText => AppText.Get("RecognizingReading");
    public string RecognitionSummaryText
    {
        get
        {
            if (_recognition is null) return string.Empty;
            var summary = _recognition.SuggestedValue.HasValue
                ? $"{AppText.Get("RecognizedText")}: {_recognition.RecognizedText}"
                : $"{AppText.Get("OcrNoNumericSuggestion")}\n{AppText.Get("RecognizedText")}: {_recognition.RecognizedText}";
            if (_recognition.Confidence is { } confidence)
                summary += $"\n{AppText.Get("RecognitionConfidence")}: {confidence.ToString("P0", AppText.Culture)}";
            return summary;
        }
    }

    public async Task InitializeAsync(CancellationToken token = default)
    {
        if (_initialized) return;
        _initialized = true;
        SetArea(await LoadAreaAsync(token));
        await LoadPhotoAsync(capture: true, token);
    }

    public async Task<NormalizedReadingArea> LoadAreaAsync(CancellationToken token = default) =>
        (await _application.GetMeterAsync(_meterId, token))?.ReadingArea ?? new NormalizedReadingArea(0.25, 0.35, 0.5, 0.3);

    public Task ReportInitializationErrorAsync(Exception exception) =>
        _dialogs.ReportErrorAsync(AppText.Get("ReadingArea"), exception);

    public void SetArea(NormalizedReadingArea area)
    {
        var x = Math.Clamp(area.X, 0, 0.92);
        var y = Math.Clamp(area.Y, 0, 0.88);
        var width = Math.Clamp(area.Width, 0.01, 1 - x);
        var height = Math.Clamp(area.Height, 0.01, 1 - y);
        _area = new NormalizedReadingArea(x, y, width, height);
        OnPropertyChanged(nameof(Area));
    }

    public async Task SaveAreaAsync(CancellationToken token = default)
    {
        try
        {
            await _application.SetReadingAreaAsync(_meterId, _area, token);
        }
        catch (Exception exception)
        {
            await _dialogs.ReportErrorAsync(AppText.Get("ReadingArea"), exception);
        }
    }

    private async Task LoadPhotoAsync(bool capture, CancellationToken token = default)
    {
        if (IsWorking) return;
        IsWorking = true;
        try
        {
            TemporaryMeterPhoto? selected;
            if (capture)
            {
                if (!await EnsureCameraPermissionAsync()) return;
                selected = await _photos.CaptureAsync(token);
            }
            else selected = await _photos.PickAsync(token);
            if (selected is null) return;

            await CleanupPhotoFilesAsync();
            _original = selected;
            _cropped = null;
            _recognition = null;
            RotationDegrees = 0;
            ReadingValue = string.Empty;
            SetArea(await LoadAreaAsync(token));
            CurrentImage = ImageSource.FromFile(selected.FilePath);
            NotifyState();
            double? rotationCorrection;
            try
            {
                rotationCorrection = await _areaSuggester.EstimateRotationCorrectionDegreesAsync(selected.FilePath, token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Could not estimate meter photo rotation: {exception}");
                rotationCorrection = null;
            }

            if (rotationCorrection.HasValue)
                RotationDegrees = rotationCorrection.Value;
            await FindReadingAreaAsync(showNoCandidate: false, token);
        }
        catch (Exception exception)
        {
            await _dialogs.ReportErrorAsync(AppText.Get("PhotoUnavailable"), exception);
        }
        finally { IsWorking = false; }
    }

    private async Task<bool> EnsureCameraPermissionAsync()
    {
        if (await _cameraPermission.IsGrantedAsync()) return true;
        await _dialogs.AlertAsync(AppText.Get("CameraPermission"), AppText.Get("CameraPermissionInfo"), AppText.Get("Continue"));
        if (_cameraPermission.ShouldShowRationale())
            await _dialogs.AlertAsync(AppText.Get("CameraPermission"), AppText.Get("CameraPermissionRationale"), AppText.Get("OK"));
        if (await _cameraPermission.RequestAsync()) return true;
        await _dialogs.AlertAsync(AppText.Get("CameraUnavailable"), AppText.Get("CameraDenied"), AppText.Get("OK"));
        return false;
    }

    private async Task FindReadingAreaAsync(bool showNoCandidate, CancellationToken token = default)
    {
        if (_original is null || (showNoCandidate && IsWorking)) return;
        var wasAlreadyWorking = IsWorking;
        if (!wasAlreadyWorking) IsWorking = true;
        try
        {
            var suggestion = await _areaSuggester.SuggestAsync(_original.FilePath, RotationDegrees, token);
            if (suggestion is null)
            {
                SetArea(NoCandidateFallbackArea);
                await SaveAreaAsync(token);
                if (showNoCandidate)
                    await _dialogs.AlertAsync(AppText.Get("AutoSelectReadingArea"),
                        AppText.Get("AutoSelectReadingAreaNoCandidate"), AppText.Get("OK"));
                return;
            }

            SetArea(suggestion);
            await SaveAreaAsync(token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            if (showNoCandidate)
                await _dialogs.ReportErrorAsync(AppText.Get("AutoSelectReadingArea"), exception);
        }
        finally
        {
            if (!wasAlreadyWorking) IsWorking = false;
        }
    }

    private async Task RecognizeReadingAsync()
    {
        if (_original is null || IsWorking) return;
        IsWorking = true;
        try
        {
            if (_cropped is not null) await _photos.DeleteAsync(_cropped);
            _cropped = await _photos.CropAsync(_original, _area, RotationDegrees);
            _recognition = null;
            ReadingValue = string.Empty;
            OnPropertyChanged(nameof(Recognition));
            NotifyState();

            var result = await _application.RecognizeReadingAsync(_meterId,
                await File.ReadAllBytesAsync(_cropped.FilePath));
            _recognition = result;
            ReadingValue = result.SuggestedValue?.ToString(AppText.Culture) ?? result.RecognizedText;
            OnPropertyChanged(nameof(Recognition));
            OnPropertyChanged(nameof(RecognitionSummaryText));
        }
        catch (Exception exception)
        {
            await _dialogs.ReportErrorAsync(AppText.Get("RecognizeReading"), exception);
        }
        finally { IsWorking = false; }
    }

    private async Task SaveReadingAsync()
    {
        if (_recognition is null || IsWorking) return;
        if (!NumericInputParser.TryDouble(ReadingValue, out var value) || !double.IsFinite(value) || value < 0)
        {
            await _dialogs.AlertAsync(AppText.Get("InvalidReading"), AppText.Get("NonNegativeNumber"), AppText.Get("OK"));
            return;
        }

        IsWorking = true;
        try
        {
            var at = DateTimeOffset.Now;
            var assessment = await _application.PreviewReadingAsync(_meterId, value, at);
            var confirmLower = false;
            if (assessment.RequiresReview)
            {
                confirmLower = await _dialogs.ConfirmAsync(AppText.Get("CheckReading"), AppText.Get("LowerReadingWarning"),
                    AppText.Get("SaveAnyway"), AppText.Get("Review"));
                if (!confirmLower) return;
            }

            await _application.AddReadingWithAreaAsync(_meterId, value, at, _area, confirmLower,
                recognizedText: _recognition.RecognizedText);
            await CleanupAsync();
            await _navigation.GoBackAsync();
        }
        catch (Exception exception) { await _dialogs.ReportErrorAsync(AppText.Get("RecognizeReading"), exception); }
        finally { IsWorking = false; }
    }

    private async Task CancelAsync()
    {
        await CleanupAsync();
        await _navigation.GoBackAsync();
    }

    public async Task CleanupAsync()
    {
        CurrentImage = null;
        await CleanupPhotoFilesAsync();
        _recognition = null;
        ReadingValue = string.Empty;
        NotifyState();
    }

    private async Task CleanupPhotoFilesAsync()
    {
        var crop = _cropped;
        var original = _original;
        _cropped = null;
        _original = null;
        if (crop is not null) await _photos.DeleteAsync(crop);
        if (original is not null) await _photos.DeleteAsync(original);
    }

    private static double NormalizeRotation(double degrees)
    {
        var normalized = degrees % 360;
        if (normalized > 180) normalized -= 360;
        if (normalized < -180) normalized += 360;
        return normalized;
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasPhoto));
        OnPropertyChanged(nameof(HasRecognition));
        OnPropertyChanged(nameof(ShowPhotoChoices));
        OnPropertyChanged(nameof(IsRecognitionVisible));
        OnPropertyChanged(nameof(IsRecognitionNotVisible));
        OnPropertyChanged(nameof(CanRecognize));
        OnPropertyChanged(nameof(CanSaveReading));
        OnPropertyChanged(nameof(CameraActionText));
        OnPropertyChanged(nameof(RecognitionSummaryText));
        NotifyCommandStates();
    }

    private void NotifyCommandStates()
    {
        ChoosePhotoCommand.ChangeCanExecute();
        ChooseGalleryPhotoCommand.ChangeCanExecute();
        FindReadingAreaCommand.ChangeCanExecute();
        RecognizeReadingCommand.ChangeCanExecute();
        SaveReadingCommand.ChangeCanExecute();
        CancelCommand.ChangeCanExecute();
    }
}
