namespace Utilities.Mobile;

using System.Globalization;
using Microsoft.Extensions.Logging;
using Utilities.Core.Application.UseCases;
using Utilities.Core.Infrastructure.Abstractions;
using Utilities.Mobile.Services;
using Utilities.Mobile.ViewModels;

public partial class MeterHistoryPage : ContentPage
{
    private readonly MeterDetailViewModel _viewModel;
    private readonly IUserDialogs _dialogs;
    private readonly ILogger<MeterDetailViewModel> _logger;
    private CancellationTokenSource? _loadCancellation;

    public MeterHistoryPage(UtilitiesApplicationService application, Guid meterId, IUserDialogs dialogs, IAppNavigation navigation,
        IReadingHistoryFileTransfer historyFiles, bool tariffs, ILogger<MeterDetailViewModel> logger)
    {
        InitializeComponent();
        _viewModel = new MeterDetailViewModel(application, meterId, dialogs, navigation, historyFiles, logger) { IsHistoryPage = true, IsReadingHistory = !tariffs };
        _dialogs = dialogs;
        _logger = logger;
        BindingContext = _viewModel;
        Title = _viewModel.HistoryTitle;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MeterDetailViewModel.ChartSamples) or nameof(MeterDetailViewModel.ShowChart))
                RefreshChart();
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        try
        {
            await _viewModel.LoadAsync(_loadCancellation.Token);
            RefreshChart();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load meter history.");
            await _dialogs.AlertAsync(AppText.Get("History"), AppText.Get("UnexpectedError"), AppText.Get("OK"));
        }
    }

    protected override void OnDisappearing()
    {
        _loadCancellation?.Cancel();
        _viewModel.CancelPendingOperations();
        base.OnDisappearing();
    }

    private void RefreshChart()
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        HistoryGraph.Drawable = new MeterHistoryChartDrawable(_viewModel.ChartSamples, dark);
        HistoryGraph.Invalidate();
    }
}

internal sealed class MeterHistoryChartDrawable(IReadOnlyList<HistoryChartSample> samples, bool dark) : IDrawable
{
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var foreground = dark ? Color.FromArgb("#E8E4EF") : Color.FromArgb("#514D5B");
        var grid = dark ? Color.FromArgb("#514D5B") : Color.FromArgb("#D9D5E0");
        canvas.StrokeSize = 1;
        canvas.StrokeColor = grid;
        var left = 42f;
        var right = Math.Max(left + 1, dirtyRect.Width - 12);
        var top = 12f;
        var bottom = Math.Max(top + 1, dirtyRect.Height - 34);
        for (var line = 0; line < 4; line++)
        {
            var y = top + (bottom - top) * line / 3f;
            canvas.DrawLine(left, y, right, y);
        }
        canvas.DrawLine(left, top, left, bottom);

        if (samples.Count == 0)
        {
            canvas.FontColor = foreground;
            canvas.FontSize = 13;
            canvas.DrawString(AppText.Get("NoChartData"), left + 8, top + 22, right - left - 16, 22,
                HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

        var min = samples.Min(item => item.Value);
        var max = samples.Max(item => item.Value);
        var span = max - min;
        if (span == 0) span = Math.Max(Math.Abs(max) * 0.1, 1);
        var points = samples.Select((item, index) =>
        {
            var x = samples.Count == 1 ? (left + right) / 2 : left + (right - left) * index / (samples.Count - 1);
            var y = bottom - (float)((item.Value - min) / span) * (bottom - top);
            return (X: x, Y: y);
        }).ToArray();

        canvas.StrokeColor = Color.FromArgb("#7653D6");
        canvas.FillColor = Color.FromArgb("#7653D6");
        canvas.StrokeSize = 3;
        for (var index = 1; index < points.Length; index++)
            canvas.DrawLine(points[index - 1].X, points[index - 1].Y, points[index].X, points[index].Y);
        foreach (var point in points) canvas.FillCircle(point.X, point.Y, 4);

        canvas.FontColor = foreground;
        canvas.FontSize = 10;
        canvas.DrawString(min.ToString("0.##", CultureInfo.CurrentCulture), 2, bottom - 8, left - 6, 18,
            HorizontalAlignment.Right, VerticalAlignment.Center);
        if (max != min)
            canvas.DrawString(max.ToString("0.##", CultureInfo.CurrentCulture), 2, top - 8, left - 6, 18,
                HorizontalAlignment.Right, VerticalAlignment.Center);
        var firstDate = samples.Min(item => item.At).ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
        var lastDate = samples.Max(item => item.At).ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
        canvas.DrawString(firstDate, left, bottom + 7, (right - left) / 2, 20, HorizontalAlignment.Left, VerticalAlignment.Center);
        canvas.DrawString(lastDate, (left + right) / 2, bottom + 7, (right - left) / 2, 20, HorizontalAlignment.Right, VerticalAlignment.Center);
    }
}
