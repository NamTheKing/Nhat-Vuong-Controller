using NhatVuong.Client.Localization;

namespace NhatVuong.Client.Services;

/// <summary>AD-8: instants arrive in UTC and are shown in campus local time, converted here and nowhere else.</summary>
public static class CampusTime
{
    private static TimeZoneInfo _zone = TimeZoneInfo.Local;

    public static void Configure(string zoneId)
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var zone)
            || (zoneId == "Asia/Ho_Chi_Minh" && TimeZoneInfo.TryFindSystemTimeZoneById("SE Asia Standard Time", out zone)))
        {
            _zone = zone!;
        }
    }

    public static DateTimeOffset ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, _zone);

    public static DateTimeOffset FromLocal(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, _zone.GetUtcOffset(unspecified)).ToUniversalTime();
    }

    public static string Format(DateTimeOffset? utc, string format = "HH:mm dd/MM") =>
        utc is { } value ? ToLocal(value).ToString(format, L.Culture) : "—";
}

/// <summary>Dialogs from view models without a page reference.</summary>
public sealed class DialogService
{
    private static Page Page => Application.Current!.Windows[0].Page!;

    public Task AlertAsync(string message, string? title = null) =>
        Page.DisplayAlertAsync(title ?? L.Get("App_Title"), message, L.Get("Common_Ok"));

    public Task ErrorAsync(Exception ex) =>
        Page.DisplayAlertAsync(L.Get("Common_Error"), ex.Message, L.Get("Common_Ok"));

    public Task<bool> ConfirmAsync(string message) =>
        Page.DisplayAlertAsync(L.Get("Common_Confirm"), message, L.Get("Common_Yes"), L.Get("Common_No"));

    public Task<string?> PromptAsync(string message, string initial = "", Keyboard? keyboard = null) =>
        Page.DisplayPromptAsync(L.Get("App_Title"), message, L.Get("Common_Ok"), L.Get("Common_Cancel"), initialValue: initial, keyboard: keyboard ?? Keyboard.Default);
}
