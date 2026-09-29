using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NhatVuong.Client.Localization;
using NhatVuong.Client.Services;
using NhatVuong.Contracts;
using NhatVuong.Contracts.Api;

namespace NhatVuong.Client.ViewModels;

/// <summary>US-08, US-10: the devices the user may see, with true state and connectivity.</summary>
public sealed partial class DevicesViewModel(ApiClient api, DialogService dialogs) : ViewModelBase(dialogs)
{
    public ObservableCollection<DeviceDto> Devices { get; } = [];

    [ObservableProperty]
    private bool isEmpty;

    [ObservableProperty]
    private DeviceDto? selected;

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async () =>
    {
        Replace(Devices, await api.GetDevicesAsync());
        IsEmpty = Devices.Count == 0;
    });

    partial void OnSelectedChanged(DeviceDto? value)
    {
        if (value is null)
        {
            return;
        }

        Selected = null;
        _ = Shell.Current.GoToAsync($"device?id={value.Id}");
    }
}

/// <summary>
/// US-06, US-07, US-09, US-10, US-11, US-14, US-24: control one unit (or its whole room) and watch its true state.
/// The server decides every permission; the UI only reflects <see cref="DeviceDto.CanControlNow"/>.
/// </summary>
public sealed partial class DeviceDetailViewModel(ApiClient api, LanControlService lan, DialogService dialogs)
    : ViewModelBase(dialogs), IQueryAttributable
{
    private Guid _deviceId;
    private bool _targetsInitialised;

    [ObservableProperty]
    private DeviceDto? device;

    [ObservableProperty]
    private double targetSetpoint = 26;

    [ObservableProperty]
    private AcMode selectedMode = AcMode.Cool;

    [ObservableProperty]
    private FanSpeed selectedFan = FanSpeed.Auto;

    [ObservableProperty]
    private bool wholeRoom;

    [ObservableProperty]
    private string? resultText;

    [ObservableProperty]
    private bool resultIsError;

    [ObservableProperty]
    private string? lanStatus;

    [ObservableProperty]
    private double minSetpoint = 16;

    [ObservableProperty]
    private double maxSetpoint = 32;

    public IReadOnlyList<AcMode> Modes { get; } = Enum.GetValues<AcMode>();

    public IReadOnlyList<FanSpeed> FanSpeeds { get; } = Enum.GetValues<FanSpeed>();

    public string TargetSetpointText => $"{TargetSetpoint.ToString("0.0", CultureInfo.InvariantCulture)} °C";

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var id) && Guid.TryParse(id?.ToString(), out var parsed))
        {
            _deviceId = parsed;
            _targetsInitialised = false;
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            var bounds = await api.GetControlBoundsAsync();
            MinSetpoint = bounds.MinSetpoint;
            MaxSetpoint = bounds.MaxSetpoint;
        }
        catch (Exception ex) when (ex is ApiException or ServerUnreachableException)
        {
        }

        await RefreshAsync();
        await PrepareLanAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        try
        {
            Device = await api.GetDeviceAsync(_deviceId);
            if (!_targetsInitialised && Device is not null)
            {
                TargetSetpoint = Device.State.Setpoint;
                SelectedMode = Device.State.Mode;
                SelectedFan = Device.State.Fan;
                _targetsInitialised = true;
            }
        }
        catch (Exception ex) when (ex is ApiException or ServerUnreachableException)
        {
            Message = ex.Message;
        }
    }

    partial void OnTargetSetpointChanged(double value) => OnPropertyChanged(nameof(TargetSetpointText));

    [RelayCommand]
    private void Increase() => TargetSetpoint = Math.Min(MaxSetpoint, TargetSetpoint + 0.5);

    [RelayCommand]
    private void Decrease() => TargetSetpoint = Math.Max(MinSetpoint, TargetSetpoint - 0.5);

    [RelayCommand]
    private Task PowerOnAsync() => SendAsync(CommandAction.PowerOn, null);

    [RelayCommand]
    private Task PowerOffAsync() => SendAsync(CommandAction.PowerOff, null);

    [RelayCommand]
    private Task ApplyTemperatureAsync() => SendAsync(CommandAction.SetTemperature, TargetSetpoint.ToString("0.0", CultureInfo.InvariantCulture));

    [RelayCommand]
    private Task ApplyModeAsync() => SendAsync(CommandAction.SetMode, SelectedMode.ToString());

    [RelayCommand]
    private Task ApplyFanAsync() => SendAsync(CommandAction.SetFanSpeed, SelectedFan.ToString());

    private async Task SendAsync(CommandAction action, string? value)
    {
        if (Device is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        ResultText = null;
        try
        {
            if (WholeRoom)
            {
                var room = await api.SendRoomCommandAsync(Device.RoomId, action, value);
                ResultIsError = room.Succeeded < room.Total;
                ResultText = L.Format("Device_Result_Room", room.Succeeded, room.Total)
                             + string.Concat(room.Items.Where(i => i.Result != CommandResult.Succeeded)
                                 .Select(i => $"\n• {i.DeviceName}: {Describe(i)}"));
            }
            else
            {
                var outcome = await api.SendCommandAsync(Device.Id, action, value);
                ResultIsError = outcome.Result != CommandResult.Succeeded;
                ResultText = Describe(outcome);
            }

            await RefreshAsync();
        }
        catch (ServerUnreachableException)
        {
            await SendOverLanAsync(action, value);
        }
        catch (ApiException ex)
        {
            ResultIsError = true;
            ResultText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>US-24-1: the server is unreachable, so go straight to the module with the pre-fetched grant.</summary>
    private async Task SendOverLanAsync(CommandAction action, string? value)
    {
        var outcome = await lan.SendAsync(Device!, action, value);
        ResultIsError = !outcome.Success;
        ResultText = outcome.Success
            ? L.Get("Device_LanUsed")
            : outcome.Error == "no-grant" ? L.Get("Device_LanUnavailable") : L.Format("Device_LanRejected", outcome.Error);
        if (outcome is { Success: true, State: { } board } && Device is not null)
        {
            Device = Device with
            {
                State = Device.State with
                {
                    Power = board.Power, Setpoint = board.Setpoint, Mode = board.Mode, Fan = board.Fan,
                    RoomTemperature = board.RoomTemperature, CompressorRunning = board.CompressorRunning, ErrorCode = board.ErrorCode,
                    ObservedAt = DateTimeOffset.UtcNow,
                },
            };
        }
    }

    private async Task PrepareLanAsync()
    {
        if (Device is null)
        {
            return;
        }

        try
        {
            var grant = await lan.PrepareAsync(Device);
            LanStatus = grant is null ? null : L.Format("Device_LanReady", CampusTime.Format(grant.ValidTo, "HH:mm"));
        }
        catch (Exception ex) when (ex is ApiException or ServerUnreachableException)
        {
            LanStatus = lan.GrantFor(Device.Id) is { } cached ? L.Format("Device_LanReady", CampusTime.Format(cached.ValidTo, "HH:mm")) : null;
        }
    }

    private static string Describe(CommandOutcomeDto outcome) => outcome.Result switch
    {
        CommandResult.Succeeded => L.Format("Device_Result_Succeeded", outcome.ElapsedMs),
        CommandResult.Rejected => L.Enum(outcome.Rejection),
        _ when outcome.Rejection != RejectionReason.None => L.Enum(outcome.Rejection),
        _ => L.Enum(outcome.Result),
    };
}
