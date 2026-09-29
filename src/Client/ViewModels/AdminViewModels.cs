using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NhatVuong.Client.Localization;
using NhatVuong.Client.Services;
using NhatVuong.Contracts;
using NhatVuong.Contracts.Api;

namespace NhatVuong.Client.ViewModels;

public sealed partial class AdminViewModel(ApiClient api, DialogService dialogs) : ViewModelBase(dialogs)
{
    [ObservableProperty]
    private string? latencyText;

    public async Task LoadAsync()
    {
        try
        {
            var stats = await api.GetLatencyAsync();
            LatencyText = stats.Count == 0
                ? null
                : L.Format("Admin_Latency", stats.Count, stats.P95Ms, L.Get(stats.WithinBudget ? "Admin_LatencyOk" : "Admin_LatencyBad"));
        }
        catch (Exception ex) when (ex is ApiException or ServerUnreachableException)
        {
            LatencyText = null;
        }
    }

    [RelayCommand]
    private Task GoAsync(string route) => Shell.Current.GoToAsync(route);
}

/// <summary>US-21: buildings and rooms.</summary>
public sealed partial class CampusViewModel(ApiClient api, DialogService dialogs) : ViewModelBase(dialogs)
{
    public ObservableCollection<BuildingDto> Buildings { get; } = [];

    public ObservableCollection<RoomDto> Rooms { get; } = [];

    [ObservableProperty]
    private BuildingDto? selectedBuilding;

    [ObservableProperty]
    private RoomDto? selectedRoom;

    [ObservableProperty]
    private string newBuildingCode = string.Empty;

    [ObservableProperty]
    private string newBuildingName = string.Empty;

    [ObservableProperty]
    private BuildingDto? newRoomBuilding;

    [ObservableProperty]
    private string newRoomCode = string.Empty;

    [ObservableProperty]
    private string newRoomName = string.Empty;

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async () =>
    {
        Replace(Buildings, await api.GetBuildingsAsync());
        Replace(Rooms, await api.GetRoomsAsync());
    });

    [RelayCommand]
    private Task AddBuildingAsync() => RunAsync(async () =>
    {
        await api.CreateBuildingAsync(new SaveBuildingRequest(NewBuildingCode, NewBuildingName));
        NewBuildingCode = NewBuildingName = string.Empty;
        IsBusy = false;
        await RefreshAsync();
    }, dialogOnError: true);

    [RelayCommand]
    private async Task AddRoomAsync()
    {
        if (NewRoomBuilding is null)
        {
            await Dialogs.AlertAsync(L.Get("Common_SelectFirst"));
            return;
        }

        await RunAsync(async () =>
        {
            await api.CreateRoomAsync(new SaveRoomRequest(NewRoomBuilding.Id, NewRoomCode, NewRoomName));
            NewRoomCode = NewRoomName = string.Empty;
            IsBusy = false;
            await RefreshAsync();
        }, dialogOnError: true);
    }

    [RelayCommand]
    private async Task DeleteBuildingAsync()
    {
        if (SelectedBuilding is null || !await Dialogs.ConfirmAsync(L.Get("Common_ConfirmDelete")))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await api.DeleteBuildingAsync(SelectedBuilding.Id);
            IsBusy = false;
            await RefreshAsync();
        }, dialogOnError: true);
    }

    /// <summary>US-21-2: the server blocks deleting a room that still has devices; the message says why.</summary>
    [RelayCommand]
    private async Task DeleteRoomAsync()
    {
        if (SelectedRoom is null || !await Dialogs.ConfirmAsync(L.Get("Common_ConfirmDelete")))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await api.DeleteRoomAsync(SelectedRoom.Id);
            IsBusy = false;
            await RefreshAsync();
        }, dialogOnError: true);
    }
}

/// <summary>US-21: user accounts.</summary>
public sealed partial class UsersViewModel(ApiClient api, DialogService dialogs) : ViewModelBase(dialogs)
{
    public ObservableCollection<UserDto> Users { get; } = [];

    public IReadOnlyList<UserRole> Roles { get; } = Enum.GetValues<UserRole>();

    [ObservableProperty]
    private UserDto? selected;

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private string fullName = string.Empty;

    [ObservableProperty]
    private UserRole role = UserRole.Lecturer;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private bool isActive = true;

    partial void OnSelectedChanged(UserDto? value)
    {
        if (value is null)
        {
            return;
        }

        Email = value.Email;
        FullName = value.FullName;
        Role = value.Role;
        IsActive = value.IsActive;
        Password = string.Empty;
    }

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async () => Replace(Users, await api.GetUsersAsync()));

    [RelayCommand]
    private void New()
    {
        Selected = null;
        Email = FullName = Password = string.Empty;
        Role = UserRole.Lecturer;
        IsActive = true;
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        var request = new SaveUserRequest(Email, FullName, Role, IsActive, string.IsNullOrEmpty(Password) ? null : Password);
        if (Selected is null)
        {
            await api.CreateUserAsync(request);
        }
        else
        {
            await api.UpdateUserAsync(Selected.Id, request);
        }

        IsBusy = false;
        New();
        await RefreshAsync();
    }, dialogOnError: true);

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is null || !await Dialogs.ConfirmAsync(L.Get("Common_ConfirmDelete")))
        {
            return;
        }

        await RunAsync(async () =>
        {
            var result = await api.DeleteUserAsync(Selected.Id);
            if (result.Deactivated)
            {
                await Dialogs.AlertAsync(L.Get("Users_Deactivated"));
            }

            IsBusy = false;
            New();
            await RefreshAsync();
        }, dialogOnError: true);
    }
}

/// <summary>US-19: register a module by its QR code and hand its MQTT credentials to the installer once.</summary>
public sealed partial class RegisterDeviceViewModel(ApiClient api, DialogService dialogs) : ViewModelBase(dialogs)
{
    public ObservableCollection<RoomDto> Rooms { get; } = [];

    public ObservableCollection<DeviceDto> Devices { get; } = [];

    [ObservableProperty]
    private string qrCode = string.Empty;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private RoomDto? selectedRoom;

    [ObservableProperty]
    private DeviceDto? selectedDevice;

    [ObservableProperty]
    private string? credentialsText;

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async () =>
    {
        Replace(Rooms, await api.GetRoomsAsync());
        Replace(Devices, await api.GetDevicesAsync());
    });

    [RelayCommand]
    private async Task ScanAsync()
    {
        var scanned = await QrScanner.ScanAsync();
        if (scanned is null)
        {
            if (!QrScanner.IsSupported)
            {
                await Dialogs.AlertAsync(L.Get("Register_ScanUnsupported"));
            }

            return;
        }

        QrCode = scanned;
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (SelectedRoom is null || string.IsNullOrWhiteSpace(QrCode))
        {
            await Dialogs.AlertAsync(L.Get("Error_Required"));
            return;
        }

        await RunAsync(async () =>
        {
            var created = await api.RegisterDeviceAsync(new RegisterDeviceRequest(QrCode, SelectedRoom.Id, string.IsNullOrWhiteSpace(Name) ? null : Name));
            CredentialsText = $"{L.Get("Register_Credentials")}\nMQTT username: {created.MqttUsername}\nMQTT password: {created.MqttPassword}";
            QrCode = Name = string.Empty;
            IsBusy = false;
            await RefreshAsync();
        }, dialogOnError: true);
    }

    [RelayCommand]
    private async Task DeleteDeviceAsync()
    {
        if (SelectedDevice is null || !await Dialogs.ConfirmAsync(L.Get("Common_ConfirmDelete")))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await api.DeleteDeviceAsync(SelectedDevice.Id);
            IsBusy = false;
            await RefreshAsync();
        }, dialogOnError: true);
    }
}

/// <summary>US-20: upload the timetable; show per-row errors when nothing was imported.</summary>
public sealed partial class TimetableImportViewModel(ApiClient api, DialogService dialogs) : ViewModelBase(dialogs)
{
    public ObservableCollection<string> Errors { get; } = [];

    [ObservableProperty]
    private string? resultText;

    [RelayCommand]
    private async Task PickAndImportAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.Android] = ["text/csv", "text/comma-separated-values", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
                [DevicePlatform.WinUI] = [".csv", ".xlsx"],
            }),
        });
        if (file is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await using var stream = await file.OpenReadAsync();
            var result = await api.ImportTimetableAsync(stream, file.FileName);
            Errors.Clear();
            if (result.Success)
            {
                ResultText = L.Format("Timetable_Imported", result.ImportedCount, result.ReplacedCount);
            }
            else
            {
                ResultText = L.Get("Timetable_Errors");
                foreach (var error in result.Errors)
                {
                    var reason = L.Get($"ImportError_{error.Code}");
                    Errors.Add(L.Format("Timetable_Row", error.Row, reason.StartsWith("ImportError_", StringComparison.Ordinal) ? error.Detail : $"{reason} — {error.Detail}"));
                }
            }
        }, dialogOnError: true);
    }
}

/// <summary>US-04: temporary access for make-up classes, seminars and events.</summary>
public sealed partial class GrantsViewModel(ApiClient api, DialogService dialogs) : ViewModelBase(dialogs)
{
    public ObservableCollection<GrantDto> Grants { get; } = [];

    public ObservableCollection<UserDto> Users { get; } = [];

    public ObservableCollection<RoomDto> Rooms { get; } = [];

    [ObservableProperty]
    private GrantDto? selected;

    [ObservableProperty]
    private UserDto? selectedUser;

    [ObservableProperty]
    private RoomDto? selectedRoom;

    [ObservableProperty]
    private DateTime? date = DateTime.Today;

    [ObservableProperty]
    private TimeSpan? startTime = new(7, 0, 0);

    [ObservableProperty]
    private TimeSpan? endTime = new(11, 0, 0);

    [ObservableProperty]
    private string note = string.Empty;

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async () =>
    {
        Replace(Grants, await api.GetGrantsAsync());
        Replace(Users, (await api.GetUsersAsync()).Where(u => u.IsActive));
        Replace(Rooms, await api.GetRoomsAsync());
    });

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (SelectedUser is null || SelectedRoom is null || Date is null || StartTime is null || EndTime is null)
        {
            await Dialogs.AlertAsync(L.Get("Error_Required"));
            return;
        }

        var from = CampusTime.FromLocal(Date.Value.Date + StartTime.Value);
        var to = CampusTime.FromLocal(Date.Value.Date + EndTime.Value);
        await RunAsync(async () =>
        {
            await api.CreateGrantAsync(new CreateGrantRequest(SelectedUser.Id, SelectedRoom.Id, from, to, Note));
            Note = string.Empty;
            IsBusy = false;
            await RefreshAsync();
        }, dialogOnError: true);
    }

    [RelayCommand]
    private async Task RevokeAsync()
    {
        if (Selected is null || !await Dialogs.ConfirmAsync(L.Get("Grants_Revoke") + "?"))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await api.RevokeGrantAsync(Selected.Id);
            IsBusy = false;
            await RefreshAsync();
        }, dialogOnError: true);
    }
}

/// <summary>Administrator-configurable parameters (working agreement #6). Nothing is hard-coded in the app.</summary>
public sealed partial class PolicyViewModel(ApiClient api, DialogService dialogs) : ViewModelBase(dialogs)
{
    [ObservableProperty]
    private int controlMarginMinutes;

    [ObservableProperty]
    private int autoOffIdleMinutes;

    [ObservableProperty]
    private int longRunAlertHours;

    [ObservableProperty]
    private double minSetpoint;

    [ObservableProperty]
    private double maxSetpoint;

    [ObservableProperty]
    private string operatingStart = "06:00";

    [ObservableProperty]
    private string operatingEnd = "22:00";

    [ObservableProperty]
    private int disconnectAlertMinutes;

    [ObservableProperty]
    private int defaultPreCoolLeadMinutes;

    [ObservableProperty]
    private int commandTimeoutSeconds;

    [ObservableProperty]
    private int stateFreshnessSeconds;

    [ObservableProperty]
    private int lecturerPrecedenceSeconds;

    [ObservableProperty]
    private int deviceOfflineThresholdSeconds;

    [ObservableProperty]
    private int maxLanGrantMinutes;

    [ObservableProperty]
    private int scheduleCatchUpMinutes;

    [ObservableProperty]
    private string campusTimeZone = string.Empty;

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async () =>
    {
        var p = await api.GetPolicyAsync();
        ControlMarginMinutes = p.ControlMarginMinutes;
        AutoOffIdleMinutes = p.AutoOffIdleMinutes;
        LongRunAlertHours = p.LongRunAlertHours;
        MinSetpoint = p.MinSetpoint;
        MaxSetpoint = p.MaxSetpoint;
        OperatingStart = p.OperatingStart.ToString("HH:mm", CultureInfo.InvariantCulture);
        OperatingEnd = p.OperatingEnd.ToString("HH:mm", CultureInfo.InvariantCulture);
        DisconnectAlertMinutes = p.DisconnectAlertMinutes;
        DefaultPreCoolLeadMinutes = p.DefaultPreCoolLeadMinutes;
        CommandTimeoutSeconds = p.CommandTimeoutSeconds;
        StateFreshnessSeconds = p.StateFreshnessSeconds;
        LecturerPrecedenceSeconds = p.LecturerPrecedenceSeconds;
        DeviceOfflineThresholdSeconds = p.DeviceOfflineThresholdSeconds;
        MaxLanGrantMinutes = p.MaxLanGrantMinutes;
        ScheduleCatchUpMinutes = p.ScheduleCatchUpMinutes;
        CampusTimeZone = p.CampusTimeZone;
    });

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!TimeOnly.TryParseExact(OperatingStart, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !TimeOnly.TryParseExact(OperatingEnd, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
        {
            await Dialogs.AlertAsync(L.Get("Error_InvalidPolicy"));
            return;
        }

        await RunAsync(async () =>
        {
            await api.SavePolicyAsync(new PolicyDto(
                ControlMarginMinutes, AutoOffIdleMinutes, LongRunAlertHours, MinSetpoint, MaxSetpoint, start, end, DisconnectAlertMinutes,
                DefaultPreCoolLeadMinutes, CommandTimeoutSeconds, StateFreshnessSeconds, LecturerPrecedenceSeconds,
                DeviceOfflineThresholdSeconds, MaxLanGrantMinutes, ScheduleCatchUpMinutes, CampusTimeZone));
            await Dialogs.AlertAsync(L.Get("Policy_Saved"));
        }, dialogOnError: true);
    }
}

/// <summary>US-22: who did what to which device, when, with what result.</summary>
public sealed partial class AuditViewModel(ApiClient api, DialogService dialogs) : ViewModelBase(dialogs)
{
    private int _page = 1;

    public ObservableCollection<AuditEntryDto> Entries { get; } = [];

    public IReadOnlyList<string> Filters { get; } =
        [L.Get("Audit_AllResults"), .. Enum.GetValues<CommandResult>().Select(r => L.Enum(r))];

    [ObservableProperty]
    private int filterIndex;

    [ObservableProperty]
    private bool hasMore;

    partial void OnFilterIndexChanged(int value) => _ = RefreshAsync();

    private CommandResult? Filter => FilterIndex > 0 ? Enum.GetValues<CommandResult>()[FilterIndex - 1] : null;

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async () =>
    {
        _page = 1;
        var page = await api.GetAuditAsync(_page, Filter);
        Replace(Entries, page.Items);
        HasMore = page.Page * page.PageSize < page.Total;
    });

    [RelayCommand]
    private Task LoadMoreAsync() => RunAsync(async () =>
    {
        var page = await api.GetAuditAsync(++_page, Filter);
        foreach (var entry in page.Items)
        {
            Entries.Add(entry);
        }

        HasMore = page.Page * page.PageSize < page.Total;
    });
}

public sealed record ReportRow(string Label, string Hours, bool IsBuilding);

/// <summary>US-23: monthly runtime per room and building; an empty month shows a message, not an error.</summary>
public sealed partial class ReportViewModel(ApiClient api, DialogService dialogs) : ViewModelBase(dialogs)
{
    public ObservableCollection<ReportRow> Rows { get; } = [];

    public IReadOnlyList<int> Years { get; } = Enumerable.Range(DateTime.Today.Year - 3, 4).Reverse().ToList();

    public IReadOnlyList<int> Months { get; } = Enumerable.Range(1, 12).ToList();

    [ObservableProperty]
    private int year = DateTime.Today.Year;

    [ObservableProperty]
    private int month = DateTime.Today.Month;

    [ObservableProperty]
    private string? summary;

    [RelayCommand]
    public Task LoadAsync() => RunAsync(async () =>
    {
        var report = await api.GetRuntimeReportAsync(Year, Month);
        Rows.Clear();
        if (!report.HasData)
        {
            Summary = L.Get("Report_NoData");
            return;
        }

        Summary = L.Format("Report_Total", report.TotalHours);
        foreach (var building in report.Buildings)
        {
            Rows.Add(new ReportRow($"{building.Code} — {building.Name}", L.Format("Report_Hours", building.Hours), true));
            foreach (var room in building.Rooms)
            {
                Rows.Add(new ReportRow($"    {room.RoomCode} — {room.RoomName}", L.Format("Report_Hours", room.Hours), false));
            }
        }
    });
}
