using System.Globalization;
using System.Resources;

namespace NhatVuong.Client.Localization;

/// <summary>
/// Every user-facing string comes from Resources/Strings/AppResources*.resx (NFR-07). Vietnamese is the neutral
/// resource; adding a language means adding one satellite .resx file.
/// </summary>
public static class L
{
    private static readonly ResourceManager Resources =
        new("NhatVuong.Client.Resources.Strings.AppResources", typeof(L).Assembly);

    public static readonly IReadOnlyList<string> SupportedLanguages = ["vi", "en"];

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("vi");

    public static void SetLanguage(string language)
    {
        Culture = CultureInfo.GetCultureInfo(SupportedLanguages.Contains(language) ? language : "vi");
        CultureInfo.CurrentUICulture = Culture;
        Preferences.Default.Set("language", Culture.TwoLetterISOLanguageName);
    }

    public static void LoadSavedLanguage() => SetLanguage(Preferences.Default.Get("language", "vi"));

    public static string Get(string key) => Resources.GetString(key, Culture) ?? key;

    public static string Format(string key, params object?[] args) => string.Format(Culture, Get(key), args);

    /// <summary>Localised name of an enum value, e.g. <c>PowerState_On</c>.</summary>
    public static string Enum<T>(T value)
        where T : struct, System.Enum => Get($"{typeof(T).Name}_{value}");

    public static string EnumObject(object? value) => value is null ? string.Empty : Get($"{value.GetType().Name}_{value}");

    /// <summary>Localises a server error code, falling back to the server's neutral message.</summary>
    public static string Error(string? code, string? fallback) =>
        code is not null && Resources.GetString($"Error_{code}", Culture) is { } text ? text : fallback ?? Get("Common_Error");
}

/// <summary>XAML: <c>Text="{loc:Tr Login_Title}"</c>.</summary>
[ContentProperty(nameof(Key))]
public sealed class TrExtension : IMarkupExtension<string>
{
    public string Key { get; set; } = string.Empty;

    public string ProvideValue(IServiceProvider serviceProvider) => L.Get(Key);

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
