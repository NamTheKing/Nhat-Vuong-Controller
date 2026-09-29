using Microsoft.EntityFrameworkCore;
using ObservedState = NhatVuong.Application.Abstractions.ObservedState;
using NhatVuong.Application.Commands;
using NhatVuong.Application.Devices;
using NhatVuong.Application.Maintenance;
using NhatVuong.Application.Notifications;
using NhatVuong.Application.Reporting;
using NhatVuong.Application.Tests.Support;
using NhatVuong.Domain;

namespace NhatVuong.Application.Tests;

/// <summary>Epic C (state), Epic E (maintenance) and US-23 reporting.</summary>
public class MaintenanceAndReportingTests
{
    private static readonly ObservedState OnAt24 = new(PowerState.On, 24, AcMode.Cool, FanSpeed.Auto, 27.5, true, null);

    [Fact(DisplayName = "US-08 / US-09: board reports update the projection; stale reports never overwrite newer ones")]
    public async Task StateReports()
    {
        await using var h = new TestHarness();
        var room = await h.AddRoomAsync("A101");
        var device = await h.AddDeviceAsync(room, "HW-1");
        var reports = h.Service<DeviceReportService>();

        await reports.HandleStateReportAsync("HW-1", OnAt24, h.Now.AddSeconds(1), "https://10.0.0.5/lan", "ABC");
        await h.Service<DeviceReportService>().HandleStateReportAsync("HW-1", OnAt24 with { Setpoint = 28 }, h.Now.AddSeconds(-30), null, null);

        var state = await h.WithDbAsync(db => db.DeviceStates.SingleAsync(s => s.DeviceId == device.Id));
        Assert.Equal(PowerState.On, state.Power);
        Assert.Equal(24, state.Setpoint);
        Assert.Equal(27.5, state.RoomTemperature);
        Assert.True(state.CompressorRunning);
        Assert.Equal(1, await h.WithDbAsync(db => db.RuntimeSessions.CountAsync(s => s.EndedAt == null)));
    }

    [Fact(DisplayName = "US-16: an error code is stored with device and time, and maintenance staff are notified once")]
    public async Task ErrorCodeCreatesIncident()
    {
        await using var h = new TestHarness();
        var staff = await h.AddUserAsync(UserRole.MaintenanceStaff, "bt@u.edu.vn");
        await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        var device = await h.AddDeviceAsync(room, "HW-1");

        await h.Service<DeviceReportService>().HandleErrorAsync("HW-1", "E5", "Compressor overload", h.Now);
        await h.Service<DeviceReportService>().HandleErrorAsync("HW-1", "E5", "Compressor overload", h.Now.AddMinutes(1));

        var incident = await h.WithDbAsync(db => db.Incidents.SingleAsync());
        Assert.Equal(device.Id, incident.DeviceId);
        Assert.Equal("E5", incident.Code);
        Assert.Equal(2, incident.OccurrenceCount);
        Assert.Single(await h.Service<NotificationService>().ListAsync(staff.Id, true));
        Assert.Single(h.Notifications.Delivered);
        Assert.Equal(Connectivity.Fault, (await h.WithDbAsync(db => db.DeviceStates.SingleAsync())).GetConnectivity(h.Now, TimeSpan.FromSeconds(30)));
    }

    [Fact(DisplayName = "US-17: resolving requires a note and records who and when")]
    public async Task ResolveIncident()
    {
        await using var h = new TestHarness();
        var staff = await h.AddUserAsync(UserRole.MaintenanceStaff, "bt@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        await h.AddDeviceAsync(room, "HW-1");
        await h.Service<DeviceReportService>().HandleErrorAsync("HW-1", "E1", null, h.Now);
        var incident = await h.WithDbAsync(db => db.Incidents.SingleAsync());
        var maintenance = h.Service<MaintenanceService>();

        await Assert.ThrowsAsync<ValidationException>(() => maintenance.ResolveAsync(incident.Id, TestHarness.ActorOf(staff), " "));
        await maintenance.ResolveAsync(incident.Id, TestHarness.ActorOf(staff), "Đã thay tụ điện");
        await Assert.ThrowsAsync<ConflictException>(() => h.Service<MaintenanceService>().ResolveAsync(incident.Id, TestHarness.ActorOf(staff), "again"));

        var view = Assert.Single(await h.Service<MaintenanceService>().ListAsync(IncidentStatus.Resolved, null));
        Assert.Equal(staff.FullName, view.ResolvedByName);
        Assert.Equal(h.Now, view.ResolvedAt);
        Assert.Equal("Đã thay tụ điện", view.ResolutionNote);
    }

    [Fact(DisplayName = "US-18-1: disconnected more than 30 minutes in working hours raises one alert")]
    public async Task ProlongedDisconnectAlerts()
    {
        await using var h = new TestHarness();
        await h.AddUserAsync(UserRole.MaintenanceStaff, "bt@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        await h.AddDeviceAsync(room, "HW-1");
        h.Time.Advance(TimeSpan.FromMinutes(2));

        // Silent for longer than twice the freshness bound → presumed disconnected since last heard (t = 0).
        Assert.Equal(1, await h.Service<MonitoringService>().DetectSilentDevicesAsync());
        h.Time.Advance(TimeSpan.FromMinutes(27));
        Assert.Equal(0, await h.Service<MonitoringService>().RaiseDisconnectAlertsAsync());
        h.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, await h.Service<MonitoringService>().RaiseDisconnectAlertsAsync());
        h.Time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(0, await h.Service<MonitoringService>().RaiseDisconnectAlertsAsync());
        Assert.Equal(IncidentKind.ProlongedDisconnect, (await h.WithDbAsync(db => db.Incidents.SingleAsync())).Kind);
    }

    [Fact(DisplayName = "US-18-2: a device that reconnects after 10 minutes raises no alert")]
    public async Task ReconnectBeforeThreshold()
    {
        await using var h = new TestHarness();
        var room = await h.AddRoomAsync("A101");
        await h.AddDeviceAsync(room, "HW-1");
        await h.Service<DeviceReportService>().HandleConnectivityAsync("HW-1", false, h.Now);
        h.Time.Advance(TimeSpan.FromMinutes(10));
        await h.Service<DeviceReportService>().HandleConnectivityAsync("HW-1", true, h.Now);
        h.Time.Advance(TimeSpan.FromMinutes(40));
        await h.TouchDevicesAsync();

        Assert.Equal(0, await h.Service<MonitoringService>().RaiseDisconnectAlertsAsync());
        Assert.Equal(0, await h.WithDbAsync(db => db.Incidents.CountAsync()));
    }

    [Fact(DisplayName = "US-18: time outside working hours does not count toward the disconnect threshold")]
    public async Task DisconnectOvernightNotCounted()
    {
        await using var h = new TestHarness();
        h.SetLocalTime(21, 0);
        var room = await h.AddRoomAsync("A101");
        await h.AddDeviceAsync(room, "HW-1");
        await h.Service<DeviceReportService>().HandleConnectivityAsync("HW-1", false, h.Now);

        h.SetLocalTime(6, 10, dayOffset: 1);
        Assert.Equal(0, await h.Service<MonitoringService>().RaiseDisconnectAlertsAsync());
        h.SetLocalTime(6, 31, dayOffset: 1);
        Assert.Equal(1, await h.Service<MonitoringService>().RaiseDisconnectAlertsAsync());
    }

    [Fact(DisplayName = "US-12-2 / FR-D4: a unit running more than N hours is recorded and alerted once")]
    public async Task LongRunAlert()
    {
        await using var h = new TestHarness();
        var admin = await h.AddUserAsync(UserRole.Administrator, "admin@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        await h.AddDeviceAsync(room, "HW-1", power: PowerState.On);

        h.Time.Advance(TimeSpan.FromHours(7.9));
        Assert.Equal(0, await h.Service<MonitoringService>().RaiseLongRunAlertsAsync());
        h.Time.Advance(TimeSpan.FromHours(0.2));
        Assert.Equal(1, await h.Service<MonitoringService>().RaiseLongRunAlertsAsync());
        Assert.Equal(0, await h.Service<MonitoringService>().RaiseLongRunAlertsAsync());
        Assert.Equal(IncidentKind.LongRun, (await h.WithDbAsync(db => db.Incidents.SingleAsync())).Kind);
        Assert.Single(await h.Service<NotificationService>().ListAsync(admin.Id, true));
    }

    [Fact(DisplayName = "US-24-2 / US-25-2 / AD-5: offline facts append to audit once and never become commands")]
    public async Task OfflineFactsReplay()
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        await h.AddDeviceAsync(room, "HW-1");
        var entry = await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));
        var on = new OfflineFact(Guid.CreateVersion7(), h.Local(8, 5), CommandAction.PowerOn, null, true, CommandSource.Lan, null, lecturer.Id, lecturer.FullName, null);
        var off = new OfflineFact(Guid.CreateVersion7(), h.Local(10, 15), CommandAction.PowerOff, null, true, CommandSource.DeviceSchedule, null, null, null, entry.Id);

        Assert.Equal(2, await h.Service<DeviceReportService>().HandleOfflineFactsAsync("HW-1", [on, off]));
        Assert.Equal(0, await h.Service<DeviceReportService>().HandleOfflineFactsAsync("HW-1", [on, off]));

        Assert.Empty(h.Devices.Sent);
        var audits = await h.WithDbAsync(db => db.AuditEntries.OrderBy(a => a.RequestedAt).ToListAsync());
        Assert.Equal([CommandSource.Lan, CommandSource.DeviceSchedule], audits.Select(a => a.Source));
        Assert.NotNull((await h.WithDbAsync(db => db.TimetableEntries.SingleAsync())).AutoOffHandledAt);
        var session = await h.WithDbAsync(db => db.RuntimeSessions.SingleAsync());
        Assert.Equal(TimeSpan.FromMinutes(130), session.EndedAt - session.StartedAt);
    }

    [Fact(DisplayName = "US-23-1: monthly runtime per room, aggregated per building")]
    public async Task MonthlyRuntime()
    {
        await using var h = new TestHarness();
        var a101 = await h.AddRoomAsync("A101", "A");
        var a102 = await h.AddRoomAsync("A102", "A");
        var b201 = await h.AddRoomAsync("B201", "B");
        await h.AddDeviceAsync(a101, "HW-1");
        await h.AddDeviceAsync(a102, "HW-2");
        await h.AddDeviceAsync(b201, "HW-3");
        var reports = h.Service<DeviceReportService>();
        var off = OnAt24 with { Power = PowerState.Off };

        await reports.HandleStateReportAsync("HW-1", OnAt24, h.Local(8), null, null);
        await h.Service<DeviceReportService>().HandleStateReportAsync("HW-1", off, h.Local(10), null, null);
        await h.Service<DeviceReportService>().HandleStateReportAsync("HW-2", OnAt24, h.Local(9), null, null);
        await h.Service<DeviceReportService>().HandleStateReportAsync("HW-2", off, h.Local(9, 30), null, null);
        await h.Service<DeviceReportService>().HandleStateReportAsync("HW-3", OnAt24, h.Local(8), null, null);
        await h.Service<DeviceReportService>().HandleStateReportAsync("HW-3", off, h.Local(11), null, null);

        var report = await h.Service<ReportingService>().GetMonthlyRuntimeAsync(2026, 10);

        Assert.True(report.HasData);
        Assert.Equal(5.5, report.TotalHours);
        var buildingA = report.Buildings.Single(b => b.Code == "A");
        Assert.Equal(2.5, buildingA.Hours);
        Assert.Equal(2, buildingA.Rooms.Single(r => r.RoomCode == "A101").Hours);
        Assert.Equal(3, report.Buildings.Single(b => b.Code == "B").Hours);
    }

    [Fact(DisplayName = "US-23-2: a month with no data answers with a no-data result, not an error")]
    public async Task EmptyMonth()
    {
        await using var h = new TestHarness();
        var report = await h.Service<ReportingService>().GetMonthlyRuntimeAsync(2026, 1);
        Assert.False(report.HasData);
        Assert.Empty(report.Buildings);
        await Assert.ThrowsAsync<ValidationException>(() => h.Service<ReportingService>().GetMonthlyRuntimeAsync(2026, 13));
    }

    [Fact(DisplayName = "US-22 / NFR-01: audit is queryable by device and result; latency statistics over recent commands")]
    public async Task AuditQueryAndLatency()
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        var device = await h.AddDeviceAsync(room, "HW-1");
        await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));
        for (var i = 0; i < 3; i++)
        {
            await h.Service<CommandService>().ExecuteAsync(new CommandRequest(TestHarness.ActorOf(lecturer), device.Id, CommandAction.SetTemperature, $"{22 + i}"));
        }

        await h.Service<CommandService>().ExecuteAsync(new CommandRequest(TestHarness.ActorOf(lecturer), device.Id, CommandAction.SetTemperature, "10"));

        var reporting = h.Service<ReportingService>();
        var all = await reporting.QueryAuditAsync(new AuditQuery(device.Id, null, null, null, null));
        Assert.Equal(4, all.Total);
        var rejected = await reporting.QueryAuditAsync(new AuditQuery(null, lecturer.Id, h.Now.AddHours(-1), h.Now.AddHours(1), CommandResult.Rejected));
        Assert.Equal(1, rejected.Total);

        var latency = await reporting.GetCommandLatencyAsync(100);
        Assert.Equal(3, latency.Count);
        Assert.True(latency.WithinBudget);
    }
}
