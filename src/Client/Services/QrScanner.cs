#if ANDROID
using NhatVuong.Client.Localization;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;
#endif

namespace NhatVuong.Client.Services;

/// <summary>US-19: camera QR scanning on Android; elsewhere the administrator types (or uses a USB scanner for) the code.</summary>
public static class QrScanner
{
    public static bool IsSupported => DeviceInfo.Platform == DevicePlatform.Android;

    public static async Task<string?> ScanAsync()
    {
#if ANDROID
        if (await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted)
        {
            return null;
        }

        var page = new ScanQrPage();
        await Application.Current!.Windows[0].Page!.Navigation.PushModalAsync(page);
        return await page.Result;
#else
        await Task.CompletedTask;
        return null;
#endif
    }
}

#if ANDROID
internal sealed class ScanQrPage : ContentPage
{
    private readonly TaskCompletionSource<string?> _result = new();

    public ScanQrPage()
    {
        var reader = new CameraBarcodeReaderView
        {
            Options = new BarcodeReaderOptions { Formats = BarcodeFormat.QrCode, AutoRotate = true, Multiple = false },
        };
        reader.BarcodesDetected += (_, e) =>
        {
            var value = e.Results.FirstOrDefault()?.Value;
            if (value is not null && _result.TrySetResult(value))
            {
                MainThread.BeginInvokeOnMainThread(async () => await Navigation.PopModalAsync());
            }
        };

        var cancel = new Button { Text = L.Get("Common_Cancel"), Margin = 12 };
        cancel.Clicked += async (_, _) =>
        {
            _result.TrySetResult(null);
            await Navigation.PopModalAsync();
        };

        var grid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        grid.Add(reader, 0, 0);
        grid.Add(cancel, 0, 1);
        Content = grid;
    }

    public Task<string?> Result => _result.Task;

    protected override bool OnBackButtonPressed()
    {
        _result.TrySetResult(null);
        return base.OnBackButtonPressed();
    }
}
#endif
