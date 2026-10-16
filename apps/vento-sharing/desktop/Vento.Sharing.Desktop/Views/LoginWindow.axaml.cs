using Avalonia.Controls;
using Avalonia.Input;
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
        SizeChanged += (_, _) =>
        {
            if (_dashboard is null) UpdateLayoutForWidth();
            else _dashboard.SetViewportSize(Bounds.Width, Bounds.Height);
        };
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
        Content = _loginContent;
        DataContext = login;
        Title = "Vento Sharing · Entrar";
        if (Bounds.Width > 0)
            UpdateLayoutForWidth();
    }

    public void ShowDashboard(MainViewModel dashboard)
    {
        _dashboard = dashboard;
        Content = new DashboardView { DataContext = dashboard };
        Title = "Vento Sharing · Salas";
        dashboard.SetViewportSize(Bounds.Width, Bounds.Height);
    }

    private void UpdateLayoutForWidth()
    {
        LoginCard.Width = System.Math.Max(0, System.Math.Min(1080, Bounds.Width - 40));
        LoginCard.MaxHeight = System.Math.Max(0, Bounds.Height - 40);
        var compact = Bounds.Width < 760;
        LoginContentGrid.ColumnDefinitions = new ColumnDefinitions(compact ? "1*" : "1*,1*");
        LoginContentGrid.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto" : "Auto");
        Grid.SetColumn(BrowserPanel, compact ? 0 : 1);
        Grid.SetRow(BrowserPanel, compact ? 1 : 0);
        BrowserPanel.Margin = compact ? new Avalonia.Thickness(16, 0, 16, 16) : new Avalonia.Thickness(0, 16, 16, 16);
    }
}
