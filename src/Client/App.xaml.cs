using NhatVuong.Client.Localization;
using NhatVuong.Client.Services;
using NhatVuong.Client.Views;

namespace NhatVuong.Client;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services, SessionService session, AppNavigator navigator)
    {
        L.LoadSavedLanguage();
        InitializeComponent();
        _services = services;
        session.Expired += (_, _) => navigator.ShowLogin();
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new NavigationPage(_services.GetRequiredService<LoginPage>())) { Title = L.Get("App_Title") };
}
