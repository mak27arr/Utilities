namespace Utilities.Mobile.Infrastructure.Preferences;

using Microsoft.Maui.Storage;
using Utilities.Mobile.Services;

public sealed class MauiUserPreferenceStore : IUserPreferenceStore
{
    public string Get(string key, string defaultValue) => Preferences.Default.Get(key, defaultValue);
    public void Set(string key, string value) => Preferences.Default.Set(key, value);
}
