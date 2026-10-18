namespace Utilities.Mobile;

using System.Diagnostics;
using Utilities.Mobile.Services;

public partial class ColorsResourceDictionary : ResourceDictionary
{
    public ColorsResourceDictionary()
    {
        var timer = Stopwatch.StartNew();
        InitializeComponent();
        StartupTiming.LogDuration("App.Resources.Colors.InitializeComponent", timer);
    }
}
