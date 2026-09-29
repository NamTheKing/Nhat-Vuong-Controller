using System.Globalization;
using System.Resources;

namespace NhatVuong.Application;

/// <summary>Server-generated user-facing text (notifications, system actor names) from resource files (NFR-07).</summary>
public static class Text
{
    private static readonly ResourceManager Resources =
        new("NhatVuong.Application.Resources.Messages", typeof(Text).Assembly);

    /// <summary>Culture for server-generated text; Vietnamese unless configured otherwise.</summary>
    public static CultureInfo Culture { get; set; } = CultureInfo.GetCultureInfo("vi");

    public static string Get(string key, params object?[] args)
    {
        var format = Resources.GetString(key, Culture) ?? key;
        return args.Length == 0 ? format : string.Format(Culture, format, args);
    }
}
