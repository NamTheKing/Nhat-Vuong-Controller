using Microsoft.EntityFrameworkCore;
using NhatVuong.Application.Administration;
using NhatVuong.Application.Devices;
using NhatVuong.Application.Identity;
using NhatVuong.Application.Policy;
using NhatVuong.Application.Tests.Support;
using NhatVuong.Contracts.Lan;
using NhatVuong.Domain;

namespace NhatVuong.Application.Tests;

/// <summary>Epic A sign-in, Epic F administration, and the LAN grant issuance of US-24.</summary>
public class AdministrationTests
{
    [Fact(DisplayName = "US-01-1: valid university credentials sign in and yield the user's role")]
    public async Task SignInValid()
    {
        await using var h = new TestHarness();
        await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn", "Correct#Pass1");

        var user = await h.Service<AuthService>().ValidateCredentialsAsync(" GV@u.edu.vn ", "Correct#Pass1");

        Assert.NotNull(user);
        Assert.Equal(UserRole.Lecturer, user.Role);
    }

    [Fact(DisplayName = "US-01-2: wrong password, unknown email or inactive account grants no access")]
    public async Task SignInInvalid()
    {
        await using var h = new TestHarness();
        await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn", "Correct#Pass1");
        await h.AddUserAsync(UserRole.Lecturer, "old@u.edu.vn", "Correct#Pass1", active: false);
        var auth = h.Service<AuthService>();

        Assert.Null(await auth.ValidateCredentialsAsync("gv@u.edu.vn", "wrong"));
        Assert.Null(await auth.ValidateCredentialsAsync("nobody@u.edu.vn", "Correct#Pass1"));
        Assert.Null(await auth.ValidateCredentialsAsync("old@u.edu.vn", "Correct#Pass1"));
        Assert.Null(await auth.ValidateCredentialsAsync("", ""));
    }

    [Fact(DisplayName = "NFR-03: passwords are stored as BCrypt hashes, never plaintext")]
    public async Task PasswordsAreBcrypt()
    {
        await using var h = new TestHarness();
        var created = await h.Service<ReferenceDataService>().CreateUserAsync("new@u.edu.vn", "New User", UserRole.Lecturer, "Secret#123");

        var stored = await h.WithDbAsync(db => db.Users.SingleAsync(u => u.Id == created.Id));
        Assert.StartsWith("$2", stored.PasswordHash);
        Assert.DoesNotContain("Secret#123", stored.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("Secret#123", stored.PasswordHash));
    }

    [Fact(DisplayName = "US-21-1: rooms, buildings and users are created and edited; duplicates refused")]
    public async Task ReferenceDataCrud()
    {
        await using var h = new TestHarness();
        var data = h.Service<ReferenceDataService>();
        var building = await data.CreateBuildingAsync("c", "Tòa C");
        var room = await data.CreateRoomAsync(building.Id, "c301", "Phòng C301");
        Assert.Equal("C301", room.Code);
        await Assert.ThrowsAsync<ConflictException>(() => data.CreateRoomAsync(building.Id, "C301", "dup"));
        await Assert.ThrowsAsync<ConflictException>(() => data.CreateBuildingAsync("C", "dup"));
        await Assert.ThrowsAsync<ValidationException>(() => data.CreateUserAsync("not-an-email", "x", UserRole.Lecturer, "Secret#123"));
        await Assert.ThrowsAsync<ValidationException>(() => data.CreateUserAsync("a@u.edu.vn", "x", UserRole.Lecturer, "short"));

        await data.UpdateRoomAsync(room.Id, building.Id, "C302", "Phòng C302");
        var rooms = await h.Service<ReferenceDataService>().ListRoomsAsync();
        Assert.Equal("C302", Assert.Single(rooms).Code);
        await Assert.ThrowsAsync<ConflictException>(() => h.Service<ReferenceDataService>().DeleteBuildingAsync(building.Id));
    }

    [Fact(DisplayName = "US-21-2: deleting a room that still has devices is blocked")]
    public async Task RoomWithDevicesCannotBeDeleted()
    {
        await using var h = new TestHarness();
        var room = await h.AddRoomAsync("A101");
        await h.AddDeviceAsync(room, "HW-1");
        var empty = await h.AddRoomAsync("A102");

        var error = await Assert.ThrowsAsync<ConflictException>(() => h.Service<ReferenceDataService>().DeleteRoomAsync(room.Id));
        Assert.Equal("RoomHasDevices", error.Code);
        await h.Service<ReferenceDataService>().DeleteRoomAsync(empty.Id);
        Assert.Equal(1, await h.WithDbAsync(db => db.Rooms.CountAsync()));
    }

    [Fact(DisplayName = "US-21: a user referenced by the timetable is deactivated rather than deleted")]
    public async Task ReferencedUserDeactivated()
    {
        await using var h = new TestHarness();
        var admin = await h.AddUserAsync(UserRole.Administrator, "admin@u.edu.vn");
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var spare = await h.AddUserAsync(UserRole.Lecturer, "spare@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(1));
        var data = h.Service<ReferenceDataService>();

        Assert.True((await data.DeleteUserAsync(lecturer.Id, TestHarness.ActorOf(admin))).Deactivated);
        Assert.True((await h.Service<ReferenceDataService>().DeleteUserAsync(spare.Id, TestHarness.ActorOf(admin))).Deleted);
        await Assert.ThrowsAsync<ConflictException>(() => h.Service<ReferenceDataService>().DeleteUserAsync(admin.Id, TestHarness.ActorOf(admin)));
        Assert.False((await h.WithDbAsync(db => db.Users.SingleAsync(u => u.Id == lecturer.Id))).IsActive);
    }

    [Theory(DisplayName = "US-19: QR payload formats")]
    [InlineData("NVC:ac-00012", "AC-00012")]
    [InlineData("nvc://device/AC-7", "AC-7")]
    [InlineData("  sim-a101-1 ", "SIM-A101-1")]
    public void QrParsing(string qr, string expected) => Assert.Equal(expected, DeviceRegistryService.ParseQrCode(qr));

    [Theory(DisplayName = "US-19: malformed QR payloads are refused")]
    [InlineData("")]
    [InlineData("NVC:")]
    [InlineData("has space")]
    [InlineData("x")]
    public void QrParsingRejects(string qr) => Assert.Throws<ValidationException>(() => DeviceRegistryService.ParseQrCode(qr));

    [Fact(DisplayName = "US-19: a new code registers into the room with its own MQTT credentials; a duplicate is refused")]
    public async Task RegisterDevice()
    {
        await using var h = new TestHarness();
        var admin = await h.AddUserAsync(UserRole.Administrator, "admin@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        var registry = h.Service<DeviceRegistryService>();

        var registered = await registry.RegisterAsync(TestHarness.ActorOf(admin), "NVC:AC-0001", room.Id, "AC trái");
        Assert.Equal("AC-0001", registered.MqttUsername);
        Assert.True(await h.Service<DeviceRegistryService>().ValidateCredentialsAsync("AC-0001", registered.MqttPassword));
        Assert.False(await h.Service<DeviceRegistryService>().ValidateCredentialsAsync("AC-0001", "wrong"));

        var duplicate = await Assert.ThrowsAsync<ConflictException>(() =>
            h.Service<DeviceRegistryService>().RegisterAsync(TestHarness.ActorOf(admin), "nvc://device/ac-0001", room.Id, null));
        Assert.Equal("DuplicateDevice", duplicate.Code);
        Assert.Equal(1, await h.WithDbAsync(db => db.Devices.CountAsync()));

        var rotated = await h.Service<DeviceRegistryService>().RotateCredentialsAsync(registered.Device.Id);
        Assert.False(await h.Service<DeviceRegistryService>().ValidateCredentialsAsync("AC-0001", registered.MqttPassword));
        Assert.True(await h.Service<DeviceRegistryService>().ValidateCredentialsAsync("AC-0001", rotated.MqttPassword));
    }

    [Fact(DisplayName = "Policy: invalid settings are refused; a new margin re-derives live grants")]
    public async Task PolicyUpdate()
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        await h.AddClassAsync(room, lecturer, h.Local(9), TimeSpan.FromHours(1));
        var service = h.Service<PolicyService>();

        var policy = await service.GetAsync();
        policy.MinSetpoint = 35;
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(policy));

        policy = await h.Service<PolicyService>().GetAsync();
        policy.ControlMarginMinutes = 30;
        await h.Service<PolicyService>().UpdateAsync(policy);

        var grant = await h.WithDbAsync(db => db.AccessGrants.SingleAsync());
        Assert.Equal(h.Local(8, 30), grant.ValidFrom);
        Assert.Equal(h.Local(10, 30), grant.ValidTo);
        Assert.Equal(30, (await h.Service<PolicyProvider>().GetAsync()).ControlMarginMinutes);
    }

    [Fact(DisplayName = "US-24 / AD-3: a LAN grant is issued only to a current rights holder, signed and bounded")]
    public async Task LanGrant()
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var other = await h.AddUserAsync(UserRole.Lecturer, "other@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        var device = await h.AddDeviceAsync(room, "HW-1");
        await h.AddClassAsync(room, lecturer, h.Local(8), TimeSpan.FromHours(4));
        using var clientKey = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var publicKey = LanSigning.ExportPublicKey(clientKey);

        var issued = await h.Service<OfflineGrantService>().IssueAsync(TestHarness.ActorOf(lecturer), device.Id, publicKey);

        Assert.True(LanSigning.TryVerifyGrant(new LanGrantDto(issued.Payload, issued.Signature, issued.Algorithm), h.Signer.PublicKey, out var claims));
        Assert.Equal("HW-1", claims!.DeviceId);
        Assert.Equal(lecturer.Id, claims.SubjectUserId);
        Assert.Equal(h.Now.AddMinutes(120), claims.ValidTo);
        Assert.False(LanSigning.TryVerifyGrant(new LanGrantDto(issued.Payload, issued.Signature, issued.Algorithm), publicKey, out _));

        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service<OfflineGrantService>().IssueAsync(TestHarness.ActorOf(other), device.Id, publicKey));
        await Assert.ThrowsAsync<ValidationException>(() => h.Service<OfflineGrantService>().IssueAsync(TestHarness.ActorOf(lecturer), device.Id, "bogus"));
    }

    [Fact(DisplayName = "US-24: LAN command signatures bind device, id, action, value and time")]
    public void LanCommandSignature()
    {
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var claims = new LanGrantClaimsDto(Guid.NewGuid(), Guid.NewGuid(), "GV", "HW-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), LanSigning.ExportPublicKey(key));
        var id = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;
        var signature = LanSigning.SignCommand(key, "HW-1", id, Contracts.CommandAction.SetTemperature, "24.0", at);
        var grant = new LanGrantDto("x", "y", "ES256");

        Assert.True(LanSigning.VerifyCommand(new LanCommandRequest(grant, id, Contracts.CommandAction.SetTemperature, "24.0", at, signature), claims));
        Assert.False(LanSigning.VerifyCommand(new LanCommandRequest(grant, id, Contracts.CommandAction.SetTemperature, "18.0", at, signature), claims));
        Assert.False(LanSigning.VerifyCommand(new LanCommandRequest(grant, Guid.NewGuid(), Contracts.CommandAction.SetTemperature, "24.0", at, signature), claims));
        Assert.False(LanSigning.VerifyCommand(new LanCommandRequest(grant, id, Contracts.CommandAction.SetTemperature, "24.0", at, "bad"), claims));
    }
}
