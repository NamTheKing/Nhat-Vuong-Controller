using Microsoft.EntityFrameworkCore;
using NhatVuong.Application.Access;
using NhatVuong.Application.Commands;
using NhatVuong.Application.Notifications;
using NhatVuong.Application.Tests.Support;
using NhatVuong.Domain;

namespace NhatVuong.Application.Tests;

/// <summary>Epic A (access) and Epic B (control) through the single command pipeline.</summary>
public class AccessAndCommandTests
{
    [Fact(DisplayName = "US-02-1 / US-06-1: the timetabled lecturer switches the unit on; state comes from the board")]
    public async Task LecturerSwitchesOn()
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        var device = await h.AddDeviceAsync(room, "HW-1");
        await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));

        var outcome = await h.Service<CommandService>().ExecuteAsync(
            new CommandRequest(TestHarness.ActorOf(lecturer), device.Id, CommandAction.PowerOn, null));

        Assert.Equal(CommandResult.Succeeded, outcome.Result);
        Assert.Equal(PowerState.On, outcome.State!.Power);
        var state = await h.WithDbAsync(db => db.DeviceStates.SingleAsync(s => s.DeviceId == device.Id));
        Assert.Equal(PowerState.On, state.Power);
        Assert.NotNull(state.OnSince);
    }

    [Fact(DisplayName = "US-02-2 / US-06-2 / NFR-05: no rights → rejected, audited, never dispatched")]
    public async Task NoRightsRejected()
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var other = await h.AddUserAsync(UserRole.Lecturer, "other@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        var device = await h.AddDeviceAsync(room, "HW-1");
        await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));

        var outcome = await h.Service<CommandService>().ExecuteAsync(
            new CommandRequest(TestHarness.ActorOf(other), device.Id, CommandAction.PowerOn, null));

        Assert.Equal(CommandResult.Rejected, outcome.Result);
        Assert.Equal(RejectionReason.NoAccess, outcome.Rejection);
        Assert.Empty(h.Devices.Sent);
        var audit = await h.WithDbAsync(db => db.AuditEntries.SingleAsync());
        Assert.Equal(CommandResult.Rejected, audit.Result);
        Assert.Equal(other.Id, audit.ActorUserId);
    }

    [Theory(DisplayName = "US-03: 08:00-10:00 class with 15 min margin — 07:46 accepted, 10:20 rejected")]
    [InlineData(7, 46, CommandResult.Succeeded)]
    [InlineData(10, 14, CommandResult.Succeeded)]
    [InlineData(7, 44, CommandResult.Rejected)]
    [InlineData(10, 20, CommandResult.Rejected)]
    public async Task ControlWindow(int hour, int minute, CommandResult expected)
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        var device = await h.AddDeviceAsync(room, "HW-1");
        await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));
        h.SetLocalTime(hour, minute);
        await h.TouchDevicesAsync();

        var outcome = await h.Service<CommandService>().ExecuteAsync(
            new CommandRequest(TestHarness.ActorOf(lecturer), device.Id, CommandAction.PowerOff, null));

        Assert.Equal(expected, outcome.Result);
    }

    [Fact(DisplayName = "US-05: rights lapse at expiry with no manual action; the sweep records it; later commands are audited")]
    public async Task ExpiryRevokesAutomatically()
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        var device = await h.AddDeviceAsync(room, "HW-1");
        await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));

        h.SetLocalTime(10, 16);
        var swept = await h.Service<AccessService>().SweepExpiredAsync(h.Now);
        Assert.Equal(1, swept);
        var grant = await h.WithDbAsync(db => db.AccessGrants.SingleAsync());
        Assert.Equal(AccessService.ExpiredReason, grant.RevokedReason);

        var outcome = await h.Service<CommandService>().ExecuteAsync(
            new CommandRequest(TestHarness.ActorOf(lecturer), device.Id, CommandAction.PowerOff, null));
        Assert.Equal(RejectionReason.NoAccess, outcome.Rejection);
        Assert.Equal(1, await h.WithDbAsync(db => db.AuditEntries.CountAsync(a => a.Result == CommandResult.Rejected)));
    }

    [Fact(DisplayName = "US-04: a temporary grant works exactly inside its range")]
    public async Task TemporaryGrant()
    {
        await using var h = new TestHarness();
        var admin = await h.AddUserAsync(UserRole.Administrator, "admin@u.edu.vn");
        var monitor = await h.AddUserAsync(UserRole.ClassMonitor, "lt@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        var device = await h.AddDeviceAsync(room, "HW-1");
        await h.Service<AccessService>().CreateTemporaryGrantAsync(
            TestHarness.ActorOf(admin), monitor.Id, room.Id, h.Local(8), h.Local(9), "Seminar");

        var inside = await h.Service<CommandService>().ExecuteAsync(
            new CommandRequest(TestHarness.ActorOf(monitor), device.Id, CommandAction.PowerOn, null));
        Assert.Equal(CommandResult.Succeeded, inside.Result);

        h.SetLocalTime(9, 1);
        await h.TouchDevicesAsync();
        var after = await h.Service<CommandService>().ExecuteAsync(
            new CommandRequest(TestHarness.ActorOf(monitor), device.Id, CommandAction.PowerOff, null));
        Assert.Equal(RejectionReason.NoAccess, after.Rejection);
    }

    [Fact(DisplayName = "US-04: invalid temporary ranges are refused")]
    public async Task TemporaryGrantValidation()
    {
        await using var h = new TestHarness();
        var admin = await h.AddUserAsync(UserRole.Administrator, "admin@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        var access = h.Service<AccessService>();
        await Assert.ThrowsAsync<ValidationException>(() =>
            access.CreateTemporaryGrantAsync(TestHarness.ActorOf(admin), admin.Id, room.Id, h.Local(9), h.Local(8), null));
        await Assert.ThrowsAsync<ValidationException>(() =>
            access.CreateTemporaryGrantAsync(TestHarness.ActorOf(admin), admin.Id, room.Id, h.Local(6), h.Local(7), null));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            access.CreateTemporaryGrantAsync(TestHarness.ActorOf(admin), Guid.NewGuid(), room.Id, h.Local(8), h.Local(9), null));
    }

    [Fact(DisplayName = "US-07-1 / US-14: in-range setpoint applied; below minimum rejected and state unchanged")]
    public async Task SetpointPolicy()
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        var device = await h.AddDeviceAsync(room, "HW-1");
        await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));
        var commands = h.Service<CommandService>();

        var ok = await commands.ExecuteAsync(new CommandRequest(TestHarness.ActorOf(lecturer), device.Id, CommandAction.SetTemperature, "24"));
        Assert.Equal(CommandResult.Succeeded, ok.Result);
        Assert.Equal(24, ok.State!.Setpoint);

        var low = await h.Service<CommandService>().ExecuteAsync(
            new CommandRequest(TestHarness.ActorOf(lecturer), device.Id, CommandAction.SetTemperature, "18"));
        Assert.Equal(RejectionReason.BelowMinimumSetpoint, low.Rejection);
        Assert.Contains("20", low.Detail);
        Assert.Single(h.Devices.Sent);
        Assert.Equal(24, (await h.WithDbAsync(db => db.DeviceStates.SingleAsync())).Setpoint);

        var mode = await h.Service<CommandService>().ExecuteAsync(
            new CommandRequest(TestHarness.ActorOf(lecturer), device.Id, CommandAction.SetMode, "dry"));
        Assert.Equal(AcMode.Dry, mode.State!.Mode);
        var fan = await h.Service<CommandService>().ExecuteAsync(
            new CommandRequest(TestHarness.ActorOf(lecturer), device.Id, CommandAction.SetFanSpeed, "High"));
        Assert.Equal(FanSpeed.High, fan.State!.Fan);
    }

    [Fact(DisplayName = "US-07-2: lecturer's command wins; the class monitor's conflicting command is rejected and they are notified")]
    public async Task LecturerPrecedenceMonitorAfter()
    {
        await using var h = new TestHarness();
        var (lecturer, monitor, device) = await SetupPrecedenceAsync(h);

        await h.Service<CommandService>().ExecuteAsync(new CommandRequest(TestHarness.ActorOf(lecturer), device, CommandAction.SetTemperature, "24"));
        var monitorOutcome = await h.Service<CommandService>().ExecuteAsync(
            new CommandRequest(TestHarness.ActorOf(monitor), device, CommandAction.SetTemperature, "27"));

        Assert.Equal(RejectionReason.LecturerPrecedence, monitorOutcome.Rejection);
        var notes = await h.Service<NotificationService>().ListAsync(monitor.Id, unreadOnly: true);
        Assert.Single(notes);
    }

    [Fact(DisplayName = "US-07-2: when the lecturer overrides a monitor's recent command, the monitor is notified")]
    public async Task LecturerPrecedenceMonitorFirst()
    {
        await using var h = new TestHarness();
        var (lecturer, monitor, device) = await SetupPrecedenceAsync(h);

        var first = await h.Service<CommandService>().ExecuteAsync(new CommandRequest(TestHarness.ActorOf(monitor), device, CommandAction.SetTemperature, "27"));
        Assert.Equal(CommandResult.Succeeded, first.Result);
        var second = await h.Service<CommandService>().ExecuteAsync(new CommandRequest(TestHarness.ActorOf(lecturer), device, CommandAction.SetTemperature, "24"));
        Assert.Equal(CommandResult.Succeeded, second.Result);
        Assert.Equal(24, second.State!.Setpoint);
        Assert.Single(await h.Service<NotificationService>().ListAsync(monitor.Id, unreadOnly: true));
    }

    [Fact(DisplayName = "FR-A5: non-conflicting monitor commands are not blocked; the window expires")]
    public async Task PrecedenceOnlyOnConflict()
    {
        await using var h = new TestHarness();
        var (lecturer, monitor, device) = await SetupPrecedenceAsync(h);
        await h.Service<CommandService>().ExecuteAsync(new CommandRequest(TestHarness.ActorOf(lecturer), device, CommandAction.SetTemperature, "24"));

        var fan = await h.Service<CommandService>().ExecuteAsync(new CommandRequest(TestHarness.ActorOf(monitor), device, CommandAction.SetFanSpeed, "Low"));
        Assert.Equal(CommandResult.Succeeded, fan.Result);

        h.Time.Advance(TimeSpan.FromMinutes(3));
        await h.TouchDevicesAsync();
        var later = await h.Service<CommandService>().ExecuteAsync(new CommandRequest(TestHarness.ActorOf(monitor), device, CommandAction.SetTemperature, "26"));
        Assert.Equal(CommandResult.Succeeded, later.Result);
    }

    [Fact(DisplayName = "US-10-2: no reply within the timeout → reported as not responding")]
    public async Task TimeoutReported()
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        var device = await h.AddDeviceAsync(room, "HW-1");
        await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));
        h.Devices.Silent["HW-1"] = true;

        var outcome = await h.Service<CommandService>().ExecuteAsync(new CommandRequest(TestHarness.ActorOf(lecturer), device.Id, CommandAction.PowerOn, null));

        Assert.Equal(CommandResult.Timeout, outcome.Result);
        Assert.Contains("5", outcome.Detail);
        Assert.Equal(CommandResult.Timeout, (await h.WithDbAsync(db => db.AuditEntries.SingleAsync())).Result);
    }

    [Fact(DisplayName = "US-10: a disconnected device fails fast; the gateway being down is reported")]
    public async Task OfflineDevice()
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        var offline = await h.AddDeviceAsync(room, "HW-1", connected: false);
        var online = await h.AddDeviceAsync(room, "HW-2");
        await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));

        var a = await h.Service<CommandService>().ExecuteAsync(new CommandRequest(TestHarness.ActorOf(lecturer), offline.Id, CommandAction.PowerOn, null));
        Assert.Equal(RejectionReason.DeviceOffline, a.Rejection);

        h.Devices.IsConnected = false;
        var b = await h.Service<CommandService>().ExecuteAsync(new CommandRequest(TestHarness.ActorOf(lecturer), online.Id, CommandAction.PowerOn, null));
        Assert.Equal(CommandResult.Failed, b.Result);
        Assert.Empty(h.Devices.Sent);
    }

    [Fact(DisplayName = "US-11: room-wide action fans out per unit and reports which succeeded and which failed")]
    public async Task RoomWide()
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        await h.AddDeviceAsync(room, "HW-1");
        await h.AddDeviceAsync(room, "HW-2");
        await h.AddDeviceAsync(room, "HW-3", connected: false);
        await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));

        var outcomes = await h.Service<CommandService>().ExecuteRoomAsync(TestHarness.ActorOf(lecturer), room.Id, CommandAction.PowerOn, null);

        Assert.Equal(3, outcomes.Count);
        Assert.Equal(2, outcomes.Count(o => o.Succeeded));
        Assert.Single(outcomes, o => o.Rejection == RejectionReason.DeviceOffline);
        Assert.Equal(3, await h.WithDbAsync(db => db.AuditEntries.CountAsync()));
        Assert.Equal(3, outcomes.Select(o => o.CommandId).Distinct().Count());
    }

    [Fact(DisplayName = "US-22-1: every audit entry carries actor, command, device, time and result")]
    public async Task AuditFields()
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        var device = await h.AddDeviceAsync(room, "HW-1");
        await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));

        await h.Service<CommandService>().ExecuteAsync(new CommandRequest(TestHarness.ActorOf(lecturer), device.Id, CommandAction.SetTemperature, "25"));

        var audit = await h.WithDbAsync(db => db.AuditEntries.SingleAsync());
        Assert.Equal(lecturer.Id, audit.ActorUserId);
        Assert.Equal(CommandAction.SetTemperature, audit.Action);
        Assert.Equal("25.0", audit.Value);
        Assert.Equal(device.Id, audit.DeviceId);
        Assert.Equal(h.Now, audit.RequestedAt);
        Assert.Equal(CommandResult.Succeeded, audit.Result);
        Assert.NotNull(audit.GrantId);
    }

    [Fact(DisplayName = "US-13-2: an on-command outside operating hours is rejected even with a valid grant")]
    public async Task OutsideHours()
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        var device = await h.AddDeviceAsync(room, "HW-1");
        await h.AddTemporaryGrantAsync(lecturer, room, h.Local(21), h.Local(23));
        h.SetLocalTime(22, 30);
        await h.TouchDevicesAsync();

        var outcome = await h.Service<CommandService>().ExecuteAsync(new CommandRequest(TestHarness.ActorOf(lecturer), device.Id, CommandAction.PowerOn, null));

        Assert.Equal(RejectionReason.OutsideOperatingHours, outcome.Rejection);
    }

    [Fact(DisplayName = "F-16: commands left Pending by a crash resolve to Failed on restart")]
    public async Task InterruptedCommandsResolve()
    {
        await using var h = new TestHarness();
        await h.WithDbAsync(async db =>
        {
            db.AuditEntries.Add(new Domain.Entities.AuditEntry
            {
                Id = Guid.CreateVersion7(), CommandId = Guid.CreateVersion7(), ActorName = "x", RequestedAt = h.Now, Result = CommandResult.Pending,
            });
            await db.SaveChangesAsync();
        });
        h.Time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(1, await h.Service<CommandService>().ResolveInterruptedAsync());
        Assert.Equal(CommandResult.Failed, (await h.WithDbAsync(db => db.AuditEntries.SingleAsync())).Result);
    }

    private static async Task<(Domain.Entities.User Lecturer, Domain.Entities.User Monitor, Guid Device)> SetupPrecedenceAsync(TestHarness h)
    {
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var monitor = await h.AddUserAsync(UserRole.ClassMonitor, "lt@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        var device = await h.AddDeviceAsync(room, "HW-1");
        await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));
        await h.AddTemporaryGrantAsync(monitor, room, h.Local(7), h.Local(11));
        return (lecturer, monitor, device.Id);
    }
}
