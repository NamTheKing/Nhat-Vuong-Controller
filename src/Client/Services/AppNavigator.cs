using NhatVuong.Client.Views;

namespace NhatVuong.Client.Services;

/// <summary>Switches the window between the sign-in page and the role-specific shell.</summary>
public sealed class AppNavigator(IServiceProvider services, SessionService session)
{
    public void ShowLogin() => SetRoot(() => new NavigationPage(services.GetRequiredService<LoginPage>()));

    public void ShowShell() => SetRoot(() => new AppShell(session, services));

    private static void SetRoot(Func<Page> create) =>
        MainThread.BeginInvokeOnMainThread(() => Application.Current!.Windows[0].Page = create());
}
