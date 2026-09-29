using NhatVuong.Client.ViewModels;

namespace NhatVuong.Client.Views;

public partial class MyClassesPage : ContentPage
{
    private readonly MyClassesViewModel _vm;

    public MyClassesPage(MyClassesViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.RefreshAsync();
    }
}

public partial class IncidentsPage : ContentPage
{
    private readonly IncidentsViewModel _vm;

    public IncidentsPage(IncidentsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.RefreshAsync();
    }
}

public partial class NotificationsPage : ContentPage
{
    private readonly NotificationsViewModel _vm;

    public NotificationsPage(NotificationsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.RefreshAsync();
    }
}

public partial class AccountPage : ContentPage
{
    public AccountPage(AccountViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}

public partial class AdminPage : ContentPage
{
    private readonly AdminViewModel _vm;

    public AdminPage(AdminViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}

public partial class CampusPage : ContentPage
{
    private readonly CampusViewModel _vm;

    public CampusPage(CampusViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.RefreshAsync();
    }
}

public partial class UsersPage : ContentPage
{
    private readonly UsersViewModel _vm;

    public UsersPage(UsersViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.RefreshAsync();
    }
}

public partial class RegisterDevicePage : ContentPage
{
    private readonly RegisterDeviceViewModel _vm;

    public RegisterDevicePage(RegisterDeviceViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.RefreshAsync();
    }
}

public partial class TimetableImportPage : ContentPage
{
    public TimetableImportPage(TimetableImportViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}

public partial class GrantsPage : ContentPage
{
    private readonly GrantsViewModel _vm;

    public GrantsPage(GrantsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.RefreshAsync();
    }
}

public partial class PolicyPage : ContentPage
{
    private readonly PolicyViewModel _vm;

    public PolicyPage(PolicyViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.RefreshAsync();
    }
}

public partial class AuditPage : ContentPage
{
    private readonly AuditViewModel _vm;

    public AuditPage(AuditViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.RefreshAsync();
    }
}

public partial class ReportPage : ContentPage
{
    private readonly ReportViewModel _vm;

    public ReportPage(ReportViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}
