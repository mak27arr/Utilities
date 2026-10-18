using Microsoft.Maui.Layouts;

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

        Overlay.SizeChanged += (_, _) => SetSelection();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(PhotoCropViewModel.CurrentPhoto) or nameof(PhotoCropViewModel.HasPhoto))
                SetSelection();
            if (args.PropertyName == nameof(PhotoCropViewModel.Area)) SetSelection();
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

        var scale = Math.Min(width / photo.PixelWidth, height / photo.PixelHeight);
        var imageWidth = photo.PixelWidth * scale;
        var imageHeight = photo.PixelHeight * scale;
        return new Rect((width - imageWidth) / 2, (height - imageHeight) / 2, imageWidth, imageHeight);
    }

    private void SetSelection()
    {
        var photo = _viewModel.CurrentPhoto;
        var imageBounds = GetDisplayedImageBounds();
        if (photo is null || imageBounds.Width <= 0 || imageBounds.Height <= 0) return;

        var bounds = ReadingAreaGeometry.GetDisplayBounds(_viewModel.Area, photo.PixelWidth, photo.PixelHeight,
            imageBounds.Width, imageBounds.Height);
        AbsoluteLayout.SetLayoutBounds(Selection,
            new Rect(imageBounds.X + bounds.X, imageBounds.Y + bounds.Y, bounds.Width, bounds.Height));
        AbsoluteLayout.SetLayoutFlags(Selection, AbsoluteLayoutFlags.None);
    }

    private void MovePanUpdated(object? sender, PanUpdatedEventArgs args)
    {
        var imageBounds = GetDisplayedImageBounds();
        if (imageBounds.Width <= 0 || imageBounds.Height <= 0) return;
        if (args.StatusType == GestureStatus.Started) _gestureStart = _viewModel.Area;
        if (args.StatusType is not (GestureStatus.Running or GestureStatus.Completed)) return;

        var x = Math.Clamp(_gestureStart.X + args.TotalX / imageBounds.Width, 0, 1 - _gestureStart.Width);
        var y = Math.Clamp(_gestureStart.Y + args.TotalY / imageBounds.Height, 0, 1 - _gestureStart.Height);
        _viewModel.SetArea(new NormalizedReadingArea(x, y, _gestureStart.Width, _gestureStart.Height));
    }

    private void ResizePanUpdated(object? sender, PanUpdatedEventArgs args)
    {
        var imageBounds = GetDisplayedImageBounds();
        if (imageBounds.Width <= 0 || imageBounds.Height <= 0) return;
        if (args.StatusType == GestureStatus.Started) _gestureStart = _viewModel.Area;
        if (args.StatusType is not (GestureStatus.Running or GestureStatus.Completed)) return;

        var width = Math.Clamp(_gestureStart.Width + args.TotalX / imageBounds.Width, 0.08, 1 - _gestureStart.X);
        var height = Math.Clamp(_gestureStart.Height + args.TotalY / imageBounds.Height, 0.12, 1 - _gestureStart.Y);
        _viewModel.SetArea(new NormalizedReadingArea(_gestureStart.X, _gestureStart.Y, width, height));
    }
}
