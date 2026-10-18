namespace Utilities.Mobile.Services;

public sealed record ServiceIconPreset(string Key, string Glyph);

public static class ServiceIconPresets
{
    // Keep these keys stable: they are persisted in the local database.
    public static IReadOnlyList<ServiceIconPreset> All { get; } =
    [
        new("drop", "💧"), new("flame", "🔥"), new("bolt", "⚡"), new("radiator", "♨️"),
        new("building", "🏢"), new("internet", "🌐"), new("waste", "♻️"), new("waterworks", "🚰"),
        new("snowflake", "❄️"), new("sun", "☀️"), new("wind", "🌬️"), new("leaf", "🍃"),
        new("home", "🏠"), new("key", "🔑"), new("cleaning", "🧹"), new("tools", "🛠️"),
        new("phone", "☎️"), new("television", "📺"), new("shield", "🛡️"), new("truck", "🚛"),
        new("calendar", "📅"), new("coin", "🪙"), new("light", "💡"), new("fan", "🌀"),
        new("pipe", "🪠"), new("fire-extinguisher", "🧯"), new("garden", "🌱"), new("pool", "🏊"),
        new("laundry", "🧺"), new("package", "📦")
    ];

    public static string Glyph(string key) => All.FirstOrDefault(item => item.Key == key)?.Glyph ?? "🔧";

    public static string GlyphForKey(string key) => key switch
    {
        "service-cold-water" => "🚰",
        "service-hot-water" => "♨️",
        "service-water" => "💧",
        "service-gas" => "🔥",
        "service-electricity" => "⚡",
        "service-heating" => "🌡️",
        "service-housing" => "🏢",
        "service-internet" => "🌐",
        "service-waste" => "♻️",
        "service-meter" => "🔢",
        "service-subscription" => "📄",
        _ => Glyph(key)
    };
}
