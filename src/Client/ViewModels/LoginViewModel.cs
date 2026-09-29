using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NhatVuong.Client.Localization;
using NhatVuong.Client.Services;

namespace NhatVuong.Client.ViewModels;

/// <summary>US-01: sign in with the university account.</summary>
public sealed partial class LoginViewModel(ApiClient api, SessionService session, AppNavigator navigator, DialogService dialogs)
    : ViewModelBase(dialogs)
{
    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private string serverUrl = ApiClient.ServerUrl;

    public async Task TryResumeAsync()
    {
        if (await session.TryResumeAsync())
        {
            await ConfigureAsync();
            navigator.ShowShell();
        }
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrEmpty(Password))
        {
            Message = L.Get("Login_Required");
            return;
        }

        await RunAsync(async () =>
        {
            ApiClient.ServerUrl = ServerUrl;
            var login = await api.LoginAsync(Email.Trim(), Password);
            await session.StartAsync(login);
            Password = string.Empty;
            await ConfigureAsync();
            navigator.ShowShell();
        });
    }

    private async Task ConfigureAsync()
    {
        try
        {
            CampusTime.Configure((await api.GetControlBoundsAsync()).CampusTimeZone);
        }
        catch (Exception ex) when (ex is ApiException or ServerUnreachableException)
        {
            // Keep the device's own time zone; everything else still works.
        }
    }
}
