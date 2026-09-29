using Microsoft.Extensions.Logging;
using NhatVuong.Client.Services;
using NhatVuong.Client.ViewModels;
using NhatVuong.Client.Views;
#if ANDROID
using ZXing.Net.Maui.Controls;
#endif

namespace NhatVuong.Client;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });
#if ANDROID
        builder.UseBarcodeReader();
#endif
#if DEBUG
        builder.Logging.AddDebug();
#endif

        var services = builder.Services;
        services.AddSingleton<SessionService>();
        services.AddSingleton<ApiClient>();
        services.AddSingleton<LanControlService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<AppNavigator>();

        Page<LoginPage, LoginViewModel>(services);
        Page<DevicesPage, DevicesViewModel>(services);
        Page<DeviceDetailPage, DeviceDetailViewModel>(services);
        Page<MyClassesPage, MyClassesViewModel>(services);
        Page<IncidentsPage, IncidentsViewModel>(services);
        Page<NotificationsPage, NotificationsViewModel>(services);
        Page<AccountPage, AccountViewModel>(services);
        Page<AdminPage, AdminViewModel>(services);
        Page<CampusPage, CampusViewModel>(services);
        Page<UsersPage, UsersViewModel>(services);
        Page<RegisterDevicePage, RegisterDeviceViewModel>(services);
        Page<TimetableImportPage, TimetableImportViewModel>(services);
        Page<GrantsPage, GrantsViewModel>(services);
        Page<PolicyPage, PolicyViewModel>(services);
        Page<AuditPage, AuditViewModel>(services);
        Page<ReportPage, ReportViewModel>(services);

        return builder.Build();
    }

    private static void Page<TPage, TViewModel>(IServiceCollection services)
        where TPage : class
        where TViewModel : class
    {
        services.AddTransient<TPage>();
        services.AddTransient<TViewModel>();
    }
}
