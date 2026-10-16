using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Vento.Sharing.Desktop.Configuration;
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
                activeLogin = login;
                login.Authenticated += (username, email, token, serverAddress) =>
                {
                    var endpoints = VentoClientConfiguration.Current;
                    var dashboard = new MainViewModel(username, email, token, endpoints.ApiBaseUrl, endpoints.HubBaseUrl);
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
