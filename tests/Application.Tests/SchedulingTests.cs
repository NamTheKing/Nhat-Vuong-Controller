using Microsoft.EntityFrameworkCore;
using NhatVuong.Application.Devices;
using NhatVuong.Application.Scheduling;
using NhatVuong.Application.Tests.Support;
using NhatVuong.Domain;

namespace NhatVuong.Application.Tests;

/// <summary>Epic D — scheduling and energy saving, driven by a fake clock.</summary>
public class SchedulingTests
{
    [Fact(DisplayName = "US-12-1: X minutes after the class ends, with no class following, the room's units switch off")]
    public async Task AutoOffAfterClass()
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        var device = await h.AddDeviceAsync(room, "HW-1", power: PowerState.On);
        await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));

        h.SetLocalTime(10, 14);
        Assert.Equal(0, await h.Service<SchedulingService>().RunAutoOffAsync());

        h.SetLocalTime(10, 15);
        Assert.Equal(1, await h.Service<SchedulingService>().RunAutoOffAsync());
        Assert.Equal(PowerState.Off, (await h.WithDbAsync(db => db.DeviceStates.SingleAsync(s => s.DeviceId == device.Id))).Power);

        var audit = await h.WithDbAsync(db => db.AuditEntries.SingleAsync());
        Assert.Equal(CommandSource.Scheduler, audit.Source);
        Assert.Null(audit.ActorUserId);

        // Runs once: a later tick does nothing more.
        h.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0, await h.Service<SchedulingService>().RunAutoOffAsync());
    }

    [Fact(DisplayName = "US-12-1: no auto-off when another class starts within X minutes")]
    public async Task NoAutoOffWhenFollowed()
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        await h.AddDeviceAsync(room, "HW-1", power: PowerState.On);
        await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));
        await h.AddClassAsync(room, lecturer, h.Local(10, 10), TimeSpan.FromHours(1));

        h.SetLocalTime(10, 16);
        Assert.Equal(0, await h.Service<SchedulingService>().RunAutoOffAsync());
        Assert.Empty(h.Devices.Sent);
    }

    [Fact(DisplayName = "US-12-1: a temporary booking right after class keeps the room on")]
    public async Task NoAutoOffDuringTemporaryBooking()
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        await h.AddDeviceAsync(room, "HW-1", power: PowerState.On);
        await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));
        await h.AddTemporaryGrantAsync(lecturer, room, h.Local(10), h.Local(12));

        h.SetLocalTime(10, 20);
        Assert.Equal(0, await h.Service<SchedulingService>().RunAutoOffAsync());
    }

    [Fact(DisplayName = "Restart safety: an auto-off missed beyond the catch-up window is skipped, not fired late")]
    public async Task MissedAutoOffSkipped()
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        await h.AddDeviceAsync(room, "HW-1", power: PowerState.On);
        var entry = await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));

        h.SetLocalTime(11, 0);
        Assert.Equal(0, await h.Service<SchedulingService>().RunAutoOffAsync());
        Assert.NotNull((await h.WithDbAsync(db => db.TimetableEntries.SingleAsync(e => e.Id == entry.Id))).AutoOffHandledAt);
    }

    [Fact(DisplayName = "US-13-1: after closing time every running unit switches off")]
    public async Task ClosingSwitchesOff()
    {
        await using var h = new TestHarness();
        var room = await h.AddRoomAsync("A101");
        await h.AddDeviceAsync(room, "HW-1", power: PowerState.On);
        await h.AddDeviceAsync(room, "HW-2", power: PowerState.On);
        await h.AddDeviceAsync(room, "HW-3");

        h.SetLocalTime(21, 59);
        Assert.Equal(0, await h.Service<SchedulingService>().RunClosingAsync());

        h.SetLocalTime(22, 0);
        Assert.Equal(2, await h.Service<SchedulingService>().RunClosingAsync());
        Assert.All(await h.WithDbAsync(db => db.DeviceStates.ToListAsync()), s => Assert.Equal(PowerState.Off, s.Power));
    }

    [Fact(DisplayName = "US-15-1: pre-cool switches the room on at class start minus lead time")]
    public async Task PreCoolFires()
    {
        await using var h = new TestHarness();
        h.SetLocalTime(7, 0);
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        await h.AddDeviceAsync(room, "HW-1");
        var entry = await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));

        var schedule = await h.Service<SchedulingService>().CreatePreCoolAsync(TestHarness.ActorOf(lecturer), entry.Id, 10);
        Assert.Equal(h.Local(7, 50), schedule.DueAt);
        Assert.Contains(h.Devices.Configs["HW-1"].Schedule, a => a.ActionId == schedule.Id && a.Action == CommandAction.PowerOn);

        h.SetLocalTime(7, 49);
        await h.TouchDevicesAsync();
        Assert.Equal(0, await h.Service<SchedulingService>().RunPreCoolAsync());

        h.SetLocalTime(7, 50);
        Assert.Equal(1, await h.Service<SchedulingService>().RunPreCoolAsync());
        Assert.Equal(PowerState.On, (await h.WithDbAsync(db => db.DeviceStates.SingleAsync())).Power);
        Assert.Equal(PreCoolStatus.Done, (await h.WithDbAsync(db => db.PreCoolSchedules.SingleAsync())).Status);
    }

    [Fact(DisplayName = "US-15-2: a cancelled pre-cool produces no command")]
    public async Task CancelledPreCoolDoesNothing()
    {
        await using var h = new TestHarness();
        h.SetLocalTime(7, 0);
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        await h.AddDeviceAsync(room, "HW-1");
        var entry = await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));
        var scheduling = h.Service<SchedulingService>();
        var schedule = await scheduling.CreatePreCoolAsync(TestHarness.ActorOf(lecturer), entry.Id, 10);

        await h.Service<SchedulingService>().CancelPreCoolAsync(TestHarness.ActorOf(lecturer), schedule.Id);
        h.SetLocalTime(7, 55);
        Assert.Equal(0, await h.Service<SchedulingService>().RunPreCoolAsync());
        Assert.Empty(h.Devices.Sent);
        Assert.DoesNotContain(h.Devices.Configs["HW-1"].Schedule, a => a.ActionId == schedule.Id);
    }

    [Fact(DisplayName = "US-15: only the class's lecturer (or a grant holder) may schedule; not in the past or outside hours")]
    public async Task PreCoolValidation()
    {
        await using var h = new TestHarness();
        h.SetLocalTime(7, 0);
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var other = await h.AddUserAsync(UserRole.Lecturer, "other@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        var entry = await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));
        var early = await h.AddClassAsync(room, lecturer, h.Local(6, 5, dayOffset: 1), TimeSpan.FromHours(1));
        var scheduling = h.Service<SchedulingService>();

        await Assert.ThrowsAsync<ForbiddenException>(() => scheduling.CreatePreCoolAsync(TestHarness.ActorOf(other), entry.Id, 10));
        await Assert.ThrowsAsync<ValidationException>(() => scheduling.CreatePreCoolAsync(TestHarness.ActorOf(lecturer), entry.Id, 90));
        await Assert.ThrowsAsync<ValidationException>(() => scheduling.CreatePreCoolAsync(TestHarness.ActorOf(lecturer), early.Id, 30));
        await scheduling.CreatePreCoolAsync(TestHarness.ActorOf(lecturer), entry.Id, 10);
        await Assert.ThrowsAsync<ConflictException>(() => h.Service<SchedulingService>().CreatePreCoolAsync(TestHarness.ActorOf(lecturer), entry.Id, 10));

        var classes = await h.Service<SchedulingService>().ListMyClassesAsync(TestHarness.ActorOf(lecturer), 7);
        Assert.Equal(2, classes.Count);
        Assert.Equal(PreCoolStatus.Pending, classes[0].PreCoolStatus);
    }

    [Fact(DisplayName = "US-25 / AD-12: the cached schedule carries absolute auto-off and closing times, never policy")]
    public async Task CachedScheduleContents()
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        await h.AddDeviceAsync(room, "HW-1");
        var entry = await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(2));

        var config = await h.Service<DeviceConfigService>().BuildAsync(room.Id);

        Assert.Contains(config.Schedule, a => a.ActionId == entry.Id && a.At == h.Local(10, 15) && a.Action == CommandAction.PowerOff);
        Assert.Contains(config.Schedule, a => a.Reason == "closing" && a.At == h.Local(22));
        Assert.Equal(h.Signer.PublicKey, config.ServerPublicKey);
        Assert.Equal(120, config.OfflineThresholdSeconds);
    }
}
