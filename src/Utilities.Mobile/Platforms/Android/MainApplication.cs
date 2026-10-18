using Android.App;
using Android.Runtime;

namespace Utilities.Mobile;

[Application]
public class MainApplication : MauiApplication
{
	internal static IServiceProvider? AppServices { get; private set; }
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	protected override MauiApp CreateMauiApp()
	{
		Android.Util.Log.Info("UtilitiesStartup", "Creating MAUI application.");
		try
		{
			var app = MauiProgram.CreateMauiApp();
			AppServices = app.Services;
			Android.Util.Log.Info("UtilitiesStartup", "MAUI application created.");
			return app;
		}
		catch (Exception exception)
		{
			Android.Util.Log.Error("UtilitiesStartup", $"MAUI application creation failed: {exception}");
			throw;
		}
	}
}

