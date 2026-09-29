using NhatVuong.Client.Localization;
using NhatVuong.Client.Services;
using NhatVuong.Client.Views;
using NhatVuong.Contracts;

namespace NhatVuong.Client;

/// <summary>Role-specific home (US-01-1): each role sees only the tabs for its functions. The server still enforces every permission.</summary>
public sealed class AppShell : Shell
{
    private static bool _routesRegistered;
    private readonly IServiceProvider _services;

    public AppShell(SessionService session, IServiceProvider services)
    {
        _services = services;
        RegisterRoutes();
        FlyoutBehavior = FlyoutBehavior.Disabled;
        Title = L.Get("App_Title");

        var tabs = new TabBar();
        tabs.Items.Add(Tab<DevicesPage>("Tab_Devices"));
        if (session.IsIn(UserRole.Lecturer))
        {
            tabs.Items.Add(Tab<MyClassesPage>("Tab_Classes"));
        }

        if (session.IsIn(UserRole.MaintenanceStaff, UserRole.Administrator))
        {
            tabs.Items.Add(Tab<IncidentsPage>("Tab_Incidents"));
        }

        if (session.IsIn(UserRole.Administrator))
        {
            tabs.Items.Add(Tab<AdminPage>("Tab_Admin"));
        }

        tabs.Items.Add(Tab<NotificationsPage>("Tab_Notifications"));
        tabs.Items.Add(Tab<AccountPage>("Tab_Account"));
        Items.Add(tabs);
    }

    private ShellContent Tab<TPage>(string titleKey)
        where TPage : Page => new()
    {
        Title = L.Get(titleKey),
        Route = typeof(TPage).Name,
        ContentTemplate = new DataTemplate(() => _services.GetRequiredService<TPage>()),
    };

    private static void RegisterRoutes()
    {
        if (_routesRegistered)
        {
            return;
        }

        _routesRegistered = true;
        Routing.RegisterRoute("device", typeof(DeviceDetailPage));
        Routing.RegisterRoute("campus", typeof(CampusPage));
        Routing.RegisterRoute("users", typeof(UsersPage));
        Routing.RegisterRoute("register-device", typeof(RegisterDevicePage));
        Routing.RegisterRoute("timetable", typeof(TimetableImportPage));
        Routing.RegisterRoute("grants", typeof(GrantsPage));
        Routing.RegisterRoute("policy", typeof(PolicyPage));
        Routing.RegisterRoute("audit", typeof(AuditPage));
        Routing.RegisterRoute("report", typeof(ReportPage));
    }
}
