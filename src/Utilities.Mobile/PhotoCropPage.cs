using Microsoft.Maui.Layouts;
#if DEBUG
using Android.Util;
#endif

namespace Utilities.Mobile;

using Utilities.Core.Application.UseCases;
using Utilities.Core.BusinessLogic;
using Utilities.Core.Domain.ValueObjects;
using Utilities.Core.Infrastructure.Abstractions;
using Utilities.Mobile.Services;
using Utilities.Mobile.ViewModels;

public partial class PhotoCropPage : ContentPage
{
    private readonly PhotoCropViewModel _viewModel;
    private NormalizedReadingArea _gestureStart = new(0.25, 0.35, 0.5, 0.3);
    private const double MinPhotoScale = 1;
    private const double MaxPhotoScale = 15;
    private const double MinReadingAreaWidthAtBaseZoom = 0.08;
    private const double MinReadingAreaHeightAtBaseZoom = 0.12;
    private double _photoScale = 1;
    private double _photoOffsetX;
    private double _photoOffsetY;
    private double _photoPanStartX;
    private double _photoPanStartY;

    public PhotoCropPage(UtilitiesApplicationService application, Guid meterId, ITemporaryMeterPhotoProvider photos,
        ICameraPermissionService cameraPermission, IUserDialogs dialogs, IAppNavigation navigation)
    {
        InitializeComponent();
        _viewModel = new PhotoCropViewModel(application, meterId, photos, cameraPermission, dialogs, navigation);
        BindingContext = _viewModel;
        Title = AppText.Get("PhotoReadingTitle");

        var move = new PanGestureRecognizer();
        move.PanUpdated += MovePanUpdated;
        MoveSurface.GestureRecognizers.Add(move);

        var resize = new PanGestureRecognizer();
        resize.PanUpdated += ResizePanUpdated;
        ResizeSurface.GestureRecognizers.Add(resize);

        var photoPan = new PanGestureRecognizer();
        photoPan.PanUpdated += PhotoPanUpdated;
        ImageViewport.GestureRecognizers.Add(photoPan);

        var photoPinch = new PinchGestureRecognizer();
        photoPinch.PinchUpdated += PhotoPinchUpdated;
        ImageViewport.GestureRecognizers.Add(photoPinch);

        Overlay.SizeChanged += (_, _) =>
        {
            SetPhotoTransform(_photoScale, _photoOffsetX, _photoOffsetY);
#if DEBUG
            LogGestureState("OverlaySizeChanged");
#endif
        };
        ImageViewport.SizeChanged += (_, _) =>
        {
            SetPhotoTransform(_photoScale, _photoOffsetX, _photoOffsetY);
#if DEBUG
            LogGestureState("ViewportSizeChanged");
#endif
        };
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PhotoCropViewModel.CurrentImage))
            {
                ResetPhotoTransform();
                SetSelection();
            }
            if (args.PropertyName is nameof(PhotoCropViewModel.CurrentPhoto) or nameof(PhotoCropViewModel.HasPhoto))
                SetSelection();
            if (args.PropertyName == nameof(PhotoCropViewModel.RotationDegrees))
            {
                PhotoImage.Rotation = _viewModel.RotationDegrees;
                SetPhotoTransform(_photoScale, _photoOffsetX, _photoOffsetY);
            }
            if (args.PropertyName == nameof(PhotoCropViewModel.Area))
            {
                SetSelection();
#if DEBUG
                Log.Debug("PhotoCropGesture",
                    $"AreaChanged; area=({_viewModel.Area.X:F3},{_viewModel.Area.Y:F3},{_viewModel.Area.Width:F3},{_viewModel.Area.Height:F3}); " +
                    $"selection={Selection.Bounds}");
#endif
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _viewModel.InitializeAsync();
            SetSelection();
        }
        catch (Exception exception)
        {
            await _viewModel.ReportInitializationErrorAsync(exception);
        }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await _viewModel.CleanupAsync();
    }

    private Rect GetDisplayedImageBounds()
    {
        var photo = _viewModel.CurrentPhoto;
        var width = Overlay.Width;
        var height = Overlay.Height;
        if (photo is null || photo.PixelWidth <= 0 || photo.PixelHeight <= 0 || width <= 0 || height <= 0)
            return Rect.Zero;

        var (rotatedWidth, rotatedHeight) = GetRotatedPixelSize(photo.PixelWidth, photo.PixelHeight);
        var fitScale = Math.Min(width / rotatedWidth, height / rotatedHeight);
        var imageWidth = rotatedWidth * fitScale;
        var imageHeight = rotatedHeight * fitScale;
        var baseX = (width - imageWidth) / 2;
        var baseY = (height - imageHeight) / 2;
        return new Rect(
            (baseX - width / 2) * _photoScale + width / 2 + _photoOffsetX,
            (baseY - height / 2) * _photoScale + height / 2 + _photoOffsetY,
            imageWidth * _photoScale,
            imageHeight * _photoScale);
    }

    private void SetSelection()
    {
        var photo = _viewModel.CurrentPhoto;
        var imageBounds = GetDisplayedImageBounds();
        if (photo is null || imageBounds.Width <= 0 || imageBounds.Height <= 0) return;

        var (rotatedWidth, rotatedHeight) = GetRotatedPixelSize(photo.PixelWidth, photo.PixelHeight);
        var bounds = ReadingAreaGeometry.GetDisplayBounds(_viewModel.Area, rotatedWidth, rotatedHeight,
            imageBounds.Width, imageBounds.Height);
        AbsoluteLayout.SetLayoutBounds(Selection,
            new Rect(imageBounds.X + bounds.X, imageBounds.Y + bounds.Y, bounds.Width, bounds.Height));
        AbsoluteLayout.SetLayoutFlags(Selection, AbsoluteLayoutFlags.None);
    }

    private void ResetPhotoTransform()
    {
        _photoScale = MinPhotoScale;
        _photoOffsetX = 0;
        _photoOffsetY = 0;
        PhotoImage.Scale = _photoScale * GetPhotoScaleCorrection();
        PhotoImage.Rotation = _viewModel.RotationDegrees;
        PhotoImage.TranslationX = 0;
        PhotoImage.TranslationY = 0;
    }

    private async void MovePanUpdated(object? sender, PanUpdatedEventArgs args)
    {
#if DEBUG
        LogPan("Move", args);
#endif
        if (args.StatusType == GestureStatus.Started)
        {
            _gestureStart = _viewModel.Area;
            return;
        }
        if (args.StatusType == GestureStatus.Completed)
        {
            await _viewModel.SaveAreaAsync();
            return;
        }
        if (args.StatusType != GestureStatus.Running) return;

        var imageBounds = GetDisplayedImageBounds();
        if (imageBounds.Width <= 0 || imageBounds.Height <= 0) return;
        var x = Math.Clamp(_gestureStart.X + args.TotalX / imageBounds.Width, 0, 1 - _gestureStart.Width);
        var y = Math.Clamp(_gestureStart.Y + args.TotalY / imageBounds.Height, 0, 1 - _gestureStart.Height);
        _viewModel.SetArea(new NormalizedReadingArea(x, y, _gestureStart.Width, _gestureStart.Height));
#if DEBUG
        LogAreaAfterGesture("Move");
#endif
    }

    private async void ResizePanUpdated(object? sender, PanUpdatedEventArgs args)
    {
#if DEBUG
        LogPan("Resize", args);
#endif
        if (args.StatusType == GestureStatus.Started)
        {
            _gestureStart = _viewModel.Area;
            return;
        }
        if (args.StatusType == GestureStatus.Completed)
        {
            await _viewModel.SaveAreaAsync();
            return;
        }
        if (args.StatusType != GestureStatus.Running) return;
        var imageBounds = GetDisplayedImageBounds();
        if (imageBounds.Width <= 0 || imageBounds.Height <= 0) return;

        var maxWidth = 1 - _gestureStart.X;
        var maxHeight = 1 - _gestureStart.Y;
        var minWidth = Math.Min(MinReadingAreaWidthAtBaseZoom / _photoScale, maxWidth);
        var minHeight = Math.Min(MinReadingAreaHeightAtBaseZoom / _photoScale, maxHeight);
        var width = Math.Clamp(_gestureStart.Width + args.TotalX / imageBounds.Width, minWidth, maxWidth);
        var height = Math.Clamp(_gestureStart.Height + args.TotalY / imageBounds.Height, minHeight, maxHeight);
        _viewModel.SetArea(new NormalizedReadingArea(_gestureStart.X, _gestureStart.Y, width, height));
#if DEBUG
        LogAreaAfterGesture("Resize");
#endif
    }

    private void PhotoPanUpdated(object? sender, PanUpdatedEventArgs args)
    {
#if DEBUG
        LogPan("PhotoPan", args);
#endif
        if (args.StatusType == GestureStatus.Started)
        {
            _photoPanStartX = _photoOffsetX;
            _photoPanStartY = _photoOffsetY;
        }
        else if (args.StatusType == GestureStatus.Running)
        {
            SetPhotoTransform(_photoScale, _photoPanStartX + args.TotalX, _photoPanStartY + args.TotalY);
        }
    }

    private void PhotoPinchUpdated(object? sender, PinchGestureUpdatedEventArgs args)
    {
        if (args.Status == GestureStatus.Started)
        {
#if DEBUG
            Log.Debug("PhotoCropGesture", $"Pinch status=Started; scale={args.Scale:F3}; appliedScale={_photoScale:F3}");
#endif
            return;
        }

        if (args.Status == GestureStatus.Running)
        {
            // Scale is the ratio between consecutive pinch updates, so apply it
            // multiplicatively to the current zoom. This supports both zoom-in
            // and zoom-out across the whole gesture.
            var previousScale = _photoScale;
            var requestedScale = previousScale * args.Scale;
            var scale = Math.Clamp(requestedScale, MinPhotoScale, MaxPhotoScale);
            var offsetScale = scale / _photoScale;
            SetPhotoTransform(scale, _photoOffsetX * offsetScale, _photoOffsetY * offsetScale);
#if DEBUG
            Log.Debug("PhotoCropGesture",
                $"Pinch status=Running; factor={args.Scale:F4}; before={previousScale:F3}; " +
                $"requested={requestedScale:F3}; applied={_photoScale:F3}; " +
                $"limits=({MinPhotoScale:F1},{MaxPhotoScale:F1}); offset=({_photoOffsetX:F1},{_photoOffsetY:F1})");
#endif
        }
#if DEBUG
        else
        {
            Log.Debug("PhotoCropGesture", $"Pinch status={args.Status}; factor={args.Scale:F4}; appliedScale={_photoScale:F3}");
        }
#endif
    }

    private void SetPhotoTransform(double scale, double offsetX, double offsetY)
    {
        var bounds = GetBaseDisplayedImageBounds();
        var width = Overlay.Width;
        var height = Overlay.Height;
        if (bounds.Width <= 0 || bounds.Height <= 0 || width <= 0 || height <= 0) return;

        var maxOffsetX = Math.Max(0, (bounds.Width * scale - width) / 2);
        var maxOffsetY = Math.Max(0, (bounds.Height * scale - height) / 2);
        _photoScale = scale;
        _photoOffsetX = Math.Clamp(offsetX, -maxOffsetX, maxOffsetX);
        _photoOffsetY = Math.Clamp(offsetY, -maxOffsetY, maxOffsetY);
        PhotoImage.Scale = _photoScale * GetPhotoScaleCorrection();
        PhotoImage.Rotation = _viewModel.RotationDegrees;
        PhotoImage.TranslationX = _photoOffsetX;
        PhotoImage.TranslationY = _photoOffsetY;
        SetSelection();
    }

    private Rect GetBaseDisplayedImageBounds()
    {
        var photo = _viewModel.CurrentPhoto;
        var width = Overlay.Width;
        var height = Overlay.Height;
        if (photo is null || photo.PixelWidth <= 0 || photo.PixelHeight <= 0 || width <= 0 || height <= 0)
            return Rect.Zero;

        var (rotatedWidth, rotatedHeight) = GetRotatedPixelSize(photo.PixelWidth, photo.PixelHeight);
        var scale = Math.Min(width / rotatedWidth, height / rotatedHeight);
        var imageWidth = rotatedWidth * scale;
        var imageHeight = rotatedHeight * scale;
        return new Rect((width - imageWidth) / 2, (height - imageHeight) / 2, imageWidth, imageHeight);
    }

    private (double Width, double Height) GetRotatedPixelSize(double width, double height)
    {
        var radians = _viewModel.RotationDegrees * Math.PI / 180;
        var cosine = Math.Abs(Math.Cos(radians));
        var sine = Math.Abs(Math.Sin(radians));
        return (width * cosine + height * sine, width * sine + height * cosine);
    }

    private double GetPhotoScaleCorrection()
    {
        var photo = _viewModel.CurrentPhoto;
        var width = Overlay.Width;
        var height = Overlay.Height;
        if (photo is null || photo.PixelWidth <= 0 || photo.PixelHeight <= 0 || width <= 0 || height <= 0) return 1;
        var (rotatedWidth, rotatedHeight) = GetRotatedPixelSize(photo.PixelWidth, photo.PixelHeight);
        var rawFitScale = Math.Min(width / photo.PixelWidth, height / photo.PixelHeight);
        var rotatedFitScale = Math.Min(width / rotatedWidth, height / rotatedHeight);
        return rotatedFitScale / rawFitScale;
    }

#if DEBUG
    private void LogPan(string target, PanUpdatedEventArgs args)
    {
        var selection = Selection.Bounds;
        Log.Debug("PhotoCropGesture",
            $"Pan target={target}; status={args.StatusType}; total=({args.TotalX:F1},{args.TotalY:F1}); " +
            $"area=({_viewModel.Area.X:F3},{_viewModel.Area.Y:F3},{_viewModel.Area.Width:F3},{_viewModel.Area.Height:F3}); " +
            $"viewport=({ImageViewport.Width:F1},{ImageViewport.Height:F1}); overlay=({Overlay.Width:F1},{Overlay.Height:F1}); " +
            $"selection=({selection.X:F1},{selection.Y:F1},{selection.Width:F1},{selection.Height:F1}); " +
            $"image={GetDisplayedImageBounds()}");
    }

    private void LogGestureState(string reason)
    {
        Log.Debug("PhotoCropGesture",
            $"{reason}; imageLoaded={_viewModel.CurrentPhoto is not null}; " +
            $"moveSurface=({MoveSurface.Width:F1},{MoveSurface.Height:F1}); " +
            $"resizeSurface=({ResizeSurface.Width:F1},{ResizeSurface.Height:F1}); " +
            $"selection={Selection.Bounds}; image={GetDisplayedImageBounds()}");
    }

    private void LogAreaAfterGesture(string target)
    {
        Log.Debug("PhotoCropGesture",
            $"Applied target={target}; area=({_viewModel.Area.X:F3},{_viewModel.Area.Y:F3},{_viewModel.Area.Width:F3},{_viewModel.Area.Height:F3})");
    }
#endif

}
