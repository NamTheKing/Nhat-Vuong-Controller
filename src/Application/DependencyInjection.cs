using Microsoft.Extensions.DependencyInjection;
using NhatVuong.Application.Access;
using NhatVuong.Application.Administration;
using NhatVuong.Application.Commands;
using NhatVuong.Application.Devices;
using NhatVuong.Application.Identity;
using NhatVuong.Application.Maintenance;
using NhatVuong.Application.Notifications;
using NhatVuong.Application.Policy;
using NhatVuong.Application.Reporting;
using NhatVuong.Application.Scheduling;
using NhatVuong.Application.Timetables;

namespace NhatVuong.Application;

public static class DependencyInjection
{
    /// <summary>Registers the application core. Ports (IDataStore, IDevicePort, INotificationPort, IGrantSigner) come from adapters.</summary>
    public static IServiceCollection AddNvcApplication(this IServiceCollection services)
    {
        services.AddSingleton<PolicyCache>();
        services.AddSingleton<DeviceLocks>();
        services.AddSingleton<DeviceCredentialCache>();

        services.AddScoped<PolicyProvider>();
        services.AddScoped<PolicyService>();
        services.AddScoped<AuthService>();
        services.AddScoped<ReferenceDataService>();
        services.AddScoped<DeviceRegistryService>();
        services.AddScoped<DeviceQueryService>();
        services.AddScoped<DeviceConfigService>();
        services.AddScoped<DeviceReportService>();
        services.AddScoped<OfflineGrantService>();
        services.AddScoped<AccessService>();
        services.AddScoped<CommandService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<MaintenanceService>();
        services.AddScoped<MonitoringService>();
        services.AddScoped<TimetableImportService>();
        services.AddScoped<SchedulingService>();
        services.AddScoped<ReportingService>();
        return services;
    }
}
