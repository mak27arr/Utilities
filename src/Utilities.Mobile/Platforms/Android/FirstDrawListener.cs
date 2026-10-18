namespace Utilities.Mobile;

using Android.Views;

internal sealed class FirstDrawListener(View view, Action<long> onFirstDraw) : Java.Lang.Object, ViewTreeObserver.IOnDrawListener
{
    private int _hasDrawn;

    public void OnDraw()
    {
        if (Interlocked.Exchange(ref _hasDrawn, 1) != 0) return;

        var processElapsedMilliseconds = Services.StartupTiming.ProcessElapsedMilliseconds;
        view.Post(() =>
        {
            var observer = view.ViewTreeObserver;
            if (observer.IsAlive) observer.RemoveOnDrawListener(this);
            onFirstDraw(processElapsedMilliseconds);
        });
    }
}
