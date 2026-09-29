using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NhatVuong.Client.Localization;
using NhatVuong.Client.Services;
using NhatVuong.Contracts;
using NhatVuong.Contracts.Api;

namespace NhatVuong.Client.ViewModels;

/// <summary>US-15: the lecturer's upcoming classes and their pre-cool schedules.</summary>
public sealed partial class MyClassesViewModel(ApiClient api, DialogService dialogs) : ViewModelBase(dialogs)
{
    public ObservableCollection<ClassDto> Classes { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SchedulePreCoolCommand), nameof(CancelPreCoolCommand))]
    private ClassDto? selected;

    [ObservableProperty]
    private bool isEmpty;

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async () =>
    {
        Replace(Classes, await api.GetMyClassesAsync());
        IsEmpty = Classes.Count == 0;
    });

    private bool CanSchedule() => Selected is { PreCoolStatus: not PreCoolStatus.Pending };

    private bool CanCancel() => Selected is { PreCoolStatus: PreCoolStatus.Pending };

    [RelayCommand(CanExecute = nameof(CanSchedule))]
    private async Task SchedulePreCoolAsync()
    {
        var answer = await Dialogs.PromptAsync(L.Get("Classes_LeadPrompt"), "10", Keyboard.Numeric);
        if (!int.TryParse(answer, out var minutes))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await api.CreatePreCoolAsync(Selected!.EntryId, minutes);
            IsBusy = false;
            await RefreshAsync();
        }, dialogOnError: true);
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private Task CancelPreCoolAsync() => RunAsync(async () =>
    {
        await api.CancelPreCoolAsync(Selected!.PreCoolId!.Value);
        IsBusy = false;
        await RefreshAsync();
    }, dialogOnError: true);
}

/// <summary>US-16, US-17, US-18: incidents for maintenance staff.</summary>
public sealed partial class IncidentsViewModel(ApiClient api, DialogService dialogs) : ViewModelBase(dialogs)
{
    public ObservableCollection<IncidentDto> Incidents { get; } = [];

    public IReadOnlyList<IncidentStatus> Statuses { get; } = Enum.GetValues<IncidentStatus>();

    [ObservableProperty]
    private IncidentStatus status = IncidentStatus.Open;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResolveCommand))]
    private IncidentDto? selected;

    partial void OnStatusChanged(IncidentStatus value) => _ = RefreshAsync();

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async () => Replace(Incidents, await api.GetIncidentsAsync(Status)));

    private bool CanResolve() => Selected is { Status: IncidentStatus.Open };

    [RelayCommand(CanExecute = nameof(CanResolve))]
    private async Task ResolveAsync()
    {
        var note = await Dialogs.PromptAsync(L.Get("Incidents_NotePrompt"));
        if (string.IsNullOrWhiteSpace(note))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await api.ResolveIncidentAsync(Selected!.Id, note);
            IsBusy = false;
            await RefreshAsync();
        }, dialogOnError: true);
    }
}

public sealed partial class NotificationsViewModel(ApiClient api, DialogService dialogs) : ViewModelBase(dialogs)
{
    public ObservableCollection<NotificationDto> Notifications { get; } = [];

    [ObservableProperty]
    private bool isEmpty;

    [RelayCommand]
    public Task RefreshAsync() => RunAsync(async () =>
    {
        Replace(Notifications, await api.GetNotificationsAsync());
        IsEmpty = Notifications.Count == 0;
    });

    [RelayCommand]
    private Task MarkAllReadAsync() => RunAsync(async () =>
    {
        await api.MarkAllReadAsync();
        IsBusy = false;
        await RefreshAsync();
    });
}

/// <summary>Profile, interface language (NFR-07) and sign-out.</summary>
public sealed partial class AccountViewModel(SessionService session, AppNavigator navigator, DialogService dialogs) : ViewModelBase(dialogs)
{
    public string FullName => session.User?.FullName ?? string.Empty;

    public string Email => session.User?.Email ?? string.Empty;

    public string RoleText => session.User is { } user ? L.Enum(user.Role) : string.Empty;

    public string Server => ApiClient.ServerUrl;

    public IReadOnlyList<string> Languages { get; } = L.SupportedLanguages.Select(code => L.Get($"Language_{code}")).ToList();

    [ObservableProperty]
    private int selectedLanguageIndex = Math.Max(0, L.SupportedLanguages.ToList().IndexOf(L.Culture.TwoLetterISOLanguageName));

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        if (value >= 0 && L.SupportedLanguages[value] != L.Culture.TwoLetterISOLanguageName)
        {
            L.SetLanguage(L.SupportedLanguages[value]);
            navigator.ShowShell();
        }
    }

    [RelayCommand]
    private void SignOut()
    {
        session.SignOut();
        navigator.ShowLogin();
    }
}
