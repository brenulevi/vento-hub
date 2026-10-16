using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vento.Sharing.Desktop.Configuration;
using Vento.Sharing.Desktop.Security;

namespace Vento.Sharing.Desktop.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string _serverAddress;
    private readonly string _hubAddress;
    private readonly bool _allowAdvancedServerSettings;
    private string _identifier = string.Empty;
    private string _password = string.Empty;
    private string _message = string.Empty;
    private bool _isBusy;
    private bool _showServerSettings;
    private bool _rememberCredentials;
    private bool _hasSavedCredentials;
    private bool _isAwaitingBrowserLogin;
    private CancellationTokenSource? _browserLoginCts;
    private bool _browserLoginCancelledByUser;

    public event Action<string, string, string, string>? Authenticated;

    public string ServerAddress => _serverAddress;
    public string HubAddress => _hubAddress;
    public bool AllowAdvancedServerSettings => _allowAdvancedServerSettings;

    public string Identifier
    {
        get => _identifier;
        set => SetProperty(ref _identifier, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public bool RememberCredentials
    {
        get => _rememberCredentials;
        set => SetProperty(ref _rememberCredentials, value);
    }

    public bool HasSavedCredentials
    {
        get => _hasSavedCredentials;
        private set => SetProperty(ref _hasSavedCredentials, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                NotifyLoginCommandsCanExecuteChanged();
        }
    }

    public bool IsAwaitingBrowserLogin
    {
        get => _isAwaitingBrowserLogin;
        private set
        {
            if (SetProperty(ref _isAwaitingBrowserLogin, value))
                CancelBrowserLoginCommand.NotifyCanExecuteChanged();
        }
    }

    public bool ShowServerSettings
    {
        get => _showServerSettings;
        private set
        {
            if (SetProperty(ref _showServerSettings, value))
                OnPropertyChanged(nameof(ServerSettingsLabel));
        }
    }

    public string ServerSettingsLabel => ShowServerSettings
        ? "▾  Configurações do servidor"
        : "▸  Configurações do servidor";

    public IAsyncRelayCommand LoginCommand { get; }
    public IAsyncRelayCommand BrowserLoginCommand { get; }
    public IAsyncRelayCommand OpenRegistrationCommand { get; }
    public IRelayCommand ToggleServerSettingsCommand { get; }
    public IRelayCommand ForgetCredentialsCommand { get; }
    public IRelayCommand CancelBrowserLoginCommand { get; }

    public LoginViewModel()
    {
        var endpoints = VentoClientConfiguration.Current;
        _serverAddress = endpoints.ApiBaseUrl;
        _hubAddress = endpoints.HubBaseUrl;
        _allowAdvancedServerSettings = endpoints.ShowAdvancedServerSettings;

        LoginCommand = new AsyncRelayCommand(AuthenticateAsync, () => !IsBusy);
        BrowserLoginCommand = new AsyncRelayCommand(() => StartBrowserLoginAsync(false), () => !IsBusy);
        OpenRegistrationCommand = new AsyncRelayCommand(() => StartBrowserLoginAsync(true), () => !IsBusy);
        ToggleServerSettingsCommand = new RelayCommand(() => ShowServerSettings = !ShowServerSettings);
        ForgetCredentialsCommand = new RelayCommand(ForgetCredentials);
        CancelBrowserLoginCommand = new RelayCommand(CancelBrowserLogin, () => IsAwaitingBrowserLogin);

        try
        {
            var saved = SavedCredentialsStore.Load();
            if (saved is not null)
            {
                Identifier = saved.Identifier;
                Password = saved.Password;
                RememberCredentials = true;
                HasSavedCredentials = true;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or System.Text.Json.JsonException)
        {
            Message = "Não foi possível ler as credenciais salvas. Você pode apagá-las e entrar novamente.";
            HasSavedCredentials = true;
        }
    }

    private void ForgetCredentials()
    {
        try
        {
            SavedCredentialsStore.Clear();
            Identifier = string.Empty;
            Password = string.Empty;
            RememberCredentials = false;
            HasSavedCredentials = false;
            Message = "Credenciais salvas removidas deste computador.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Message = "Não foi possível remover as credenciais salvas.";
        }
    }

    private void CancelBrowserLogin()
    {
        if (!IsAwaitingBrowserLogin)
            return;
        _browserLoginCancelledByUser = true;
        _browserLoginCts?.Cancel();
    }

    private void NotifyLoginCommandsCanExecuteChanged()
    {
        LoginCommand.NotifyCanExecuteChanged();
        BrowserLoginCommand.NotifyCanExecuteChanged();
        OpenRegistrationCommand.NotifyCanExecuteChanged();
    }

    public async Task StartBrowserLoginAsync(bool register = false)
    {
        if (!TryCreateServiceUris(out var server, out var hub))
            return;

        if (IsAwaitingBrowserLogin)
            CancelBrowserLogin();

        _browserLoginCancelledByUser = false;
        _browserLoginCts?.Dispose();
        _browserLoginCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var cancellationToken = _browserLoginCts.Token;

        IsBusy = true;
        IsAwaitingBrowserLogin = true;
        Message = "Aguardando login no navegador… Use Cancelar se fechou a aba do navegador.";
        HttpListener? listener = null;
        try
        {
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var state = Base64Url(RandomNumberGenerator.GetBytes(24));
            using var portPicker = new TcpListener(IPAddress.Loopback, 0);
            portPicker.Start();
            var port = ((IPEndPoint)portPicker.LocalEndpoint).Port;
            portPicker.Stop();

            listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            using var stopRegistration = cancellationToken.Register(() =>
            {
                try { listener.Stop(); } catch (ObjectDisposedException) { } catch (HttpListenerException) { }
            });
            var callback = $"http://127.0.0.1:{port}/callback";
            var connect = new Uri(new Uri(hub.ToString().TrimEnd('/') + "/"),
                $"desktop/conectar?callback={Uri.EscapeDataString(callback)}&challenge={challenge}&state={state}");
            var target = register
                ? new Uri(new Uri(hub.ToString().TrimEnd('/') + "/"), "criar-conta?returnUrl=" + Uri.EscapeDataString(connect.PathAndQuery))
                : connect;
            if (Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true }) is null)
            {
                Message = "Não foi possível abrir o navegador.";
                return;
            }

            var context = await listener.GetContextAsync().WaitAsync(cancellationToken);
            var valid = context.Request.Url?.AbsolutePath == "/callback"
                && context.Request.QueryString["state"] == state;
            var code = valid ? context.Request.QueryString["code"] : null;
            var message = valid && !string.IsNullOrWhiteSpace(code)
                ? "Login concluído. Pode voltar ao Vento Sharing."
                : "Não foi possível validar a conexão. Volte ao aplicativo e tente novamente.";
            var html = $"<!doctype html><html lang=\"pt-BR\"><meta charset=\"utf-8\"><title>Vento Sharing</title><body><h1>{message}</h1></body></html>";
            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
            if (string.IsNullOrWhiteSpace(code))
            {
                Message = "Retorno do navegador inválido.";
                return;
            }

            var baseUri = new Uri(server.ToString().TrimEnd('/') + "/");
            using var exchange = await Client.PostAsJsonAsync(new Uri(baseUri, "auth/desktop/token"),
                new { code, codeVerifier = verifier });
            if (!exchange.IsSuccessStatusCode)
            {
                Message = "O código de login expirou ou já foi usado. Tente novamente.";
                return;
            }
            var tokens = await exchange.Content.ReadFromJsonAsync<LoginResponse>();
            if (string.IsNullOrWhiteSpace(tokens?.AccessToken))
            {
                Message = "O servidor não retornou uma sessão válida.";
                return;
            }
            await FinishLoginAsync(baseUri, tokens.AccessToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Message = _browserLoginCancelledByUser
                ? "Login no navegador cancelado."
                : "O login no navegador expirou. Tente novamente.";
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or PlatformNotSupportedException or HttpListenerException or SocketException or IOException or HttpRequestException)
        {
            Message = "Não foi possível concluir o login no navegador.";
        }
        finally
        {
            if (listener is not null)
            {
                try { listener.Stop(); } catch (ObjectDisposedException) { } catch (HttpListenerException) { }
                listener.Close();
            }
            _browserLoginCts?.Dispose();
            _browserLoginCts = null;
            IsAwaitingBrowserLogin = false;
            IsBusy = false;
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task AuthenticateAsync()
    {
        if (!TryCreateServiceUris(out var server, out _))
            return;

        var identifier = Identifier.Trim();
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(Password))
        {
            Message = "Preencha o usuário ou e-mail e a senha.";
            return;
        }

        IsBusy = true;
        LoginCommand.NotifyCanExecuteChanged();
        Message = "Entrando...";

        try
        {
            var baseUri = new Uri(server.ToString().TrimEnd('/') + "/");
            using var login = await Client.PostAsJsonAsync(new Uri(baseUri, "auth/login"),
                new { identifier, password = Password });
            if (!login.IsSuccessStatusCode)
            {
                Message = login.StatusCode == HttpStatusCode.Unauthorized
                    ? "Usuário, e-mail ou senha incorretos."
                    : $"Falha no login ({(int)login.StatusCode}).";
                return;
            }

            var tokens = await login.Content.ReadFromJsonAsync<LoginResponse>();
            if (string.IsNullOrEmpty(tokens?.AccessToken))
            {
                Message = "O servidor não retornou uma sessão válida.";
                return;
            }

            await FinishLoginAsync(baseUri, tokens.AccessToken, directLogin: true);
        }
        catch (HttpRequestException)
        {
            Message = "Não foi possível conectar ao servidor Vento.";
        }
        catch (TaskCanceledException)
        {
            Message = "O servidor demorou para responder.";
        }
        finally
        {
            IsBusy = false;
            LoginCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task FinishLoginAsync(Uri baseUri, string accessToken, bool directLogin = false)
    {
        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "auth/me"));
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var profileResponse = await Client.SendAsync(profileRequest);
        profileResponse.EnsureSuccessStatusCode();
        var account = await profileResponse.Content.ReadFromJsonAsync<AccountResponse>();
        if (account is null)
        {
            Message = "O servidor não retornou os dados da conta.";
            return;
        }
        if (directLogin)
        {
            try
            {
                if (RememberCredentials)
                {
                    SavedCredentialsStore.Save(new SavedCredentials(Identifier.Trim(), Password));
                    HasSavedCredentials = true;
                }
                else
                {
                    SavedCredentialsStore.Clear();
                    HasSavedCredentials = false;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
            {
                Message = "Login realizado, mas não foi possível salvar as credenciais neste computador.";
            }
        }
        Password = string.Empty;
        Authenticated?.Invoke(account.Username, account.Email, accessToken, baseUri.ToString());
    }

    private bool TryCreateServiceUris(out Uri server, out Uri hub)
    {
        server = null!;
        hub = null!;
        if (!Uri.TryCreate(ServerAddress, UriKind.Absolute, out var api)
            || (api.Scheme != Uri.UriSchemeHttp && api.Scheme != Uri.UriSchemeHttps)
            || !Uri.TryCreate(HubAddress, UriKind.Absolute, out var hubUri)
            || (hubUri.Scheme != Uri.UriSchemeHttp && hubUri.Scheme != Uri.UriSchemeHttps))
        {
            Message = AllowAdvancedServerSettings
                ? "Configure endereços válidos para o hub e a API Vento."
                : "Não foi possível resolver os servidores Vento. Atualize o aplicativo ou tente mais tarde.";
            if (AllowAdvancedServerSettings)
                ShowServerSettings = true;
            return false;
        }

        server = api;
        hub = hubUri;
        return true;
    }

    private sealed record LoginResponse(string AccessToken);
    private sealed record AccountResponse(string Username, string Email);
}
