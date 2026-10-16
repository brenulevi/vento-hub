using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Vento.Sharing.Desktop.ViewModels;

namespace Vento.Sharing.Desktop.Views;

public partial class LoginWindow : Window
{
    private readonly object? _loginContent;
    private MainViewModel? _dashboard;

    public LoginWindow()
    {
        InitializeComponent();
        Closed += (_, _) => _ = _dashboard?.StopSharingAsync();
        _loginContent = Content;
        IdentifierInput.KeyDown += SubmitLoginOnEnter;
        PasswordInput.KeyDown += SubmitLoginOnEnter;
        Loaded += (_, _) => UpdateLayoutForViewport();
        SizeChanged += (_, _) =>
        {
            if (_dashboard is null) UpdateLayoutForViewport();
            else _dashboard.SetViewportSize(Bounds.Width, Bounds.Height);
        };
        LoginFormPanel.LayoutUpdated += (_, _) => UpdateLoginScroll();
    }

    private void SubmitLoginOnEnter(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || _dashboard is not null || DataContext is not LoginViewModel login)
            return;

        login.Identifier = IdentifierInput.Text ?? string.Empty;
        login.Password = PasswordInput.Text ?? string.Empty;
        if (login.LoginCommand.CanExecute(null))
            login.LoginCommand.Execute(null);
        args.Handled = true;
    }

    public void ShowLogin(LoginViewModel login)
    {
        _ = _dashboard?.StopSharingAsync();
        _dashboard = null;
        if (WindowState == WindowState.FullScreen)
            WindowState = WindowState.Normal;
        Content = _loginContent;
        DataContext = login;
        Title = "Vento Sharing · Entrar";
        UpdateLayoutForViewport();
    }

    public void ShowDashboard(MainViewModel dashboard)
    {
        _dashboard = dashboard;
        Content = new DashboardView { DataContext = dashboard };
        Title = "Vento Sharing · Salas";
        dashboard.SetViewportSize(Bounds.Width, Bounds.Height);
    }

    private void UpdateLayoutForViewport()
    {
        if (LoginRootGrid is null || LoginFormPanel is null || BrowserPanel is null || LoginLeftHost is null
            || LoginFormCenterHost is null)
            return;

        var width = ClientSize.Width > 0 ? ClientSize.Width : Bounds.Width;
        var height = ClientSize.Height > 0 ? ClientSize.Height : Bounds.Height;
        if (height <= 0) return;

        var compact = width < 760;
        LoginRootGrid.ColumnDefinitions = compact ? new ColumnDefinitions("1*") : new ColumnDefinitions("*,380");
        LoginRootGrid.RowDefinitions = compact ? new RowDefinitions("Auto,*") : new RowDefinitions("*");

        Grid.SetColumn(LoginLeftHost, 0);
        Grid.SetRow(LoginLeftHost, 0);
        Grid.SetColumn(BrowserPanel, compact ? 0 : 1);
        Grid.SetRow(BrowserPanel, compact ? 1 : 0);

        LoginFormPanel.Margin = compact ? new Thickness(24, 20, 24, 12) : new Thickness(48, 32);
        LoginFormPanel.HorizontalAlignment = compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        BrowserPanel.Padding = compact ? new Thickness(24, 20) : new Thickness(32);

        Dispatcher.UIThread.Post(SyncLoginFormCenterHeight, DispatcherPriority.Background);
    }

    private void SyncLoginFormCenterHeight()
    {
        if (LoginFormScroll is null || LoginFormCenterHost is null)
            return;

        var viewport = LoginFormScroll.Viewport.Height;
        LoginFormCenterHost.MinHeight = viewport > 0 ? viewport : 0;
        UpdateLoginScroll();
    }

    private void UpdateLoginScroll()
    {
        if (LoginFormScroll is null || LoginFormCenterHost is null)
            return;

        var viewport = LoginFormScroll.Viewport.Height;
        if (viewport <= 0)
        {
            LoginFormScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            return;
        }

        LoginFormScroll.VerticalScrollBarVisibility =
            LoginFormCenterHost.Bounds.Height > viewport + 1
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled;
    }
}
