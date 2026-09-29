using NhatVuong.Client.ViewModels;

namespace NhatVuong.Client.Views;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _vm;

    public LoginPage(LoginViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.TryResumeAsync();
    }
}

/// <summary>Refreshes every 5 s while visible, so remote-control changes appear well within 10 s (US-09).</summary>
public partial class DevicesPage : ContentPage
{
    private readonly DevicesViewModel _vm;
    private IDispatcherTimer? _timer;

    public DevicesPage(DevicesViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.RefreshAsync();
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(5);
        _timer.Tick += async (_, _) => await _vm.RefreshAsync();
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        _timer?.Stop();
        base.OnDisappearing();
    }
}

/// <summary>Polls the device every 3 s while open (US-09: remote changes visible within 10 s).</summary>
public partial class DeviceDetailPage : ContentPage
{
    private readonly DeviceDetailViewModel _vm;
    private IDispatcherTimer? _timer;

    public DeviceDetailPage(DeviceDetailViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(3);
        _timer.Tick += async (_, _) =>
        {
            if (!_vm.IsBusy)
            {
                await _vm.RefreshAsync();
            }
        };
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        _timer?.Stop();
        base.OnDisappearing();
    }
}
