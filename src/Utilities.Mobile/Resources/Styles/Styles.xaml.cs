namespace Utilities.Mobile;

using System.Diagnostics;
using Utilities.Mobile.Services;

public partial class StylesResourceDictionary : ResourceDictionary
{
    public StylesResourceDictionary()
    {
        var timer = Stopwatch.StartNew();
        InitializeComponent();
        StartupTiming.LogDuration("App.Resources.Styles.InitializeComponent", timer);
    }
}
