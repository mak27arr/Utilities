namespace Utilities.Mobile.Infrastructure.Preferences;

using Utilities.Mobile.Services;

public sealed class MauiAppAppearance : IAppAppearance
{
    public void ApplyTheme(string theme)
    {
        if (Application.Current is { } application)
            application.UserAppTheme = theme switch
            {
                "light" => AppTheme.Light,
                "dark" => AppTheme.Dark,
                _ => AppTheme.Unspecified
            };
    }
}
