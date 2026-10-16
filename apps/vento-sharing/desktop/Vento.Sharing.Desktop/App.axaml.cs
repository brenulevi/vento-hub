using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Vento.Sharing.Desktop.ViewModels;
using Vento.Sharing.Desktop.Views;

namespace Vento.Sharing.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var loginWindow = new LoginWindow();
            LoginViewModel? activeLogin = null;

            LoginViewModel ActivateLogin()
            {
                AppTheme.Apply("graphite");
                var login = new LoginViewModel();
                if (activeLogin is not null && !login.RememberCredentials)
                {
                    login.ServerAddress = activeLogin.ServerAddress;
                    login.HubAddress = activeLogin.HubAddress;
                }
                activeLogin = login;
                login.Authenticated += (username, email, token, serverAddress) =>
                {
                    var dashboard = new MainViewModel(username, email, token, serverAddress, login.HubAddress);
                    dashboard.LogoutRequested += () => ActivateLogin();
                    loginWindow.ShowDashboard(dashboard);
                    _ = dashboard.LoadRoomsAsync();
                };
                loginWindow.ShowLogin(login);
                return login;
            }

            var loginViewModel = ActivateLogin();
            desktop.MainWindow = loginWindow;
            if (desktop.Args?.Length > 0 && desktop.Args[0].StartsWith("vento-sharing://login", System.StringComparison.OrdinalIgnoreCase))
                Dispatcher.UIThread.Post(() => _ = loginViewModel.StartBrowserLoginAsync());
        }

        base.OnFrameworkInitializationCompleted();
    }
}
