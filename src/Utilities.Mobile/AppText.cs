namespace Utilities.Mobile;

using System.Globalization;
using System.Resources;

public static class AppText
{
    private static readonly ResourceManager Resources = new("Utilities.Mobile.Resources.Strings.AppResources", typeof(AppText).Assembly);
    private static CultureInfo _culture = CultureInfo.GetCultureInfo("en");

    public static CultureInfo Culture => _culture;

    public static void Initialize(string language) => SetLanguage(language);

    public static void SetLanguage(string language)
    {
        var normalized = language.Equals("uk", StringComparison.OrdinalIgnoreCase) ? "uk" : "en";
        _culture = CultureInfo.GetCultureInfo(normalized);
        CultureInfo.DefaultThreadCurrentCulture = _culture;
        CultureInfo.DefaultThreadCurrentUICulture = _culture;
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _culture;
    }

    public static string Get(string key, params object?[] arguments)
    {
        var value = Resources.GetString(key, _culture) ?? key;
        return arguments.Length == 0 ? value : string.Format(_culture, value, arguments);
    }
}

