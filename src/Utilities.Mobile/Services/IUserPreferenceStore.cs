namespace Utilities.Mobile.Services;

public interface IUserPreferenceStore
{
    string Get(string key, string defaultValue);
    void Set(string key, string value);
}

public interface IAppAppearance
{
    void ApplyTheme(string theme);
}
