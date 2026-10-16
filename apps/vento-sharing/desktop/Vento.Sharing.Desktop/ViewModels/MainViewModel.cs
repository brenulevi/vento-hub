using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Vento.Sharing.Desktop;
using Vento.Sharing.Desktop.Configuration;
using Vento.Sharing.Desktop.Security;
using Vento.Sharing.Desktop.Media;

namespace Vento.Sharing.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Uri _server;
    private readonly Uri _hub;
    private NativePublisherSession? _publisher;
    private IntPtr _hostWindowHandle;
    private DesktopRoomViewer? _viewer;
    private long _viewerGeneration;
    private WriteableBitmap? _sharedScreenImage;
    private int _screenWidth;
    private int _screenHeight;
    private string _viewerMessage = string.Empty;
    private Guid? _sharingRoomId;
    private Guid? _sharingSessionId;
    private bool _shareBusy;
    private bool _isSharing;
    private string _shareMessage = string.Empty;
    private string _shareStreamProfile = string.Empty;
    private bool _isProfileOpen;
    private bool _isProfilePage;
    private bool _isRoomComposerOpen;
    private double _composerWidth = 500;
    private double _composerMaxHeight = 600;
    private bool _isBusy;
    private string _newRoomName = string.Empty;
    private string _inviteCodeInput = string.Empty;
    private string _message = string.Empty;
    private RoomTileViewModel? _selectedRoom;
    private readonly DispatcherTimer _refreshTimer;
    private bool _isRefreshing;
    private string _friendUsernameInput = string.Empty;
    private string _friendMessage = string.Empty;
    private string _imageMessage = string.Empty;
    private Bitmap? _avatarImage;
    private string _username;
    private string _editableUsername;
    private string _profileMessage = string.Empty;
    private string _selectedThemeId = "graphite";
    private bool _isSavingTheme;
    private bool _isWideLayout = true;
    private bool _isVideoFullscreen;

    public MainViewModel() : this("Conta Vento", string.Empty, string.Empty,
        VentoClientConfiguration.Current.ApiBaseUrl, VentoClientConfiguration.Current.HubBaseUrl) { }

    public MainViewModel(string username, string email, string accessToken, string serverAddress, string hubAddress)
    {
        _username = username;
        _editableUsername = username;
        Email = email;
        AccessToken = accessToken;
        _server = new Uri(serverAddress.TrimEnd('/') + "/");
        _hub = new Uri(hubAddress.TrimEnd('/') + "/");
        _refreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background,
            (_, _) => _ = RefreshRoomsAsync());

        ShowHomeCommand = new RelayCommand(ShowHome);
        OpenWebRoomCommand = new RelayCommand(OpenWebRoom);
        StartSharingCommand = new AsyncRelayCommand(StartSharingAsync,
            () => SelectedRoom is not null && !IsProfilePage && _publisher is null && !_shareBusy);
        StopSharingCommand = new AsyncRelayCommand(StopSharingAsync,
            () => _publisher is not null && !_shareBusy);
        OpenProfilePageCommand = new RelayCommand(OpenProfilePage);
        SaveUsernameCommand = new AsyncRelayCommand(SaveUsernameAsync, () => !IsBusy);
        AddFriendCommand = new AsyncRelayCommand(AddFriendAsync, () => !IsBusy);

        OpenRoomComposerCommand = new RelayCommand(() =>
        {
            IsProfileOpen = false;
            Message = string.Empty;
            IsRoomComposerOpen = true;
        });
        CloseRoomComposerCommand = new RelayCommand(() => IsRoomComposerOpen = false);
        ToggleProfileCommand = new RelayCommand(() => IsProfileOpen = !IsProfileOpen);
        CloseProfileCommand = new RelayCommand(() => IsProfileOpen = false);
        LogoutCommand = new RelayCommand(() =>
        {
            _ = StopSharingAsync();
            StopRoomViewer();
            _refreshTimer.Stop();
            if (SelectedRoom is not null)
                _ = LeaveRoomAsync(SelectedRoom.Id);
            IsProfileOpen = false;
            LogoutRequested?.Invoke();
        });
        CreateRoomCommand = new AsyncRelayCommand(CreateRoomAsync, () => !IsBusy);
        JoinRoomCommand = new AsyncRelayCommand(JoinRoomAsync, () => !IsBusy);
        EnterVideoFullscreenCommand = new RelayCommand(EnterVideoFullscreen, () => HasSharedScreen && !IsVideoFullscreen);
        ExitVideoFullscreenCommand = new RelayCommand(ExitVideoFullscreen, () => IsVideoFullscreen);
        foreach (var palette in AppTheme.Palettes)
            ThemeChoices.Add(new ThemeChoiceViewModel(palette, () => _ = SelectThemeAsync(palette.Id)));
        AppTheme.Apply(_selectedThemeId);

    }

    public string Username
    {
        get => _username;
        private set
        {
            if (SetProperty(ref _username, value))
                OnPropertyChanged(nameof(AvatarLetter));
        }
    }
    public string Email { get; }
    public string AccessToken { get; }
    public string SelectedRoomWebUrl => SelectedRoom is null ? string.Empty
        : new Uri(_hub, $"sharing/{SelectedRoom.Id}?invite={Uri.EscapeDataString(SelectedRoom.InviteCode)}").ToString();
    public string HubLinkHint => _hub.IsLoopback
        ? "Este link só funciona nesta máquina. Configure o endereço público do Hub no login para convidar amigos."
        : "Quem abrir o link deverá entrar com a conta Vento; o convite adiciona a pessoa à sala.";
    public bool IsSharing
    {
        get => _isSharing;
        private set
        {
            if (SetProperty(ref _isSharing, value))
                NotifySharePresentationState();
        }
    }
    public string ShareMessage
    {
        get => _shareMessage;
        private set => SetProperty(ref _shareMessage, value);
    }

    public string ShareStreamProfile
    {
        get => _shareStreamProfile;
        private set
        {
            if (SetProperty(ref _shareStreamProfile, value))
                OnPropertyChanged(nameof(HasShareStreamProfile));
        }
    }

    public bool HasShareStreamProfile => !string.IsNullOrEmpty(ShareStreamProfile);
    public void ReportRoomMessage(string message) => Message = message;

    public void SetHostWindowHandle(IntPtr handle) => _hostWindowHandle = handle;
    public event Action? LogoutRequested;
    public string AvatarLetter => Username.Length > 0 ? Username[..1].ToUpperInvariant() : "V";
    public Bitmap? AvatarImage
    {
        get => _avatarImage;
        private set
        {
            if (SetProperty(ref _avatarImage, value))
            {
                OnPropertyChanged(nameof(HasAvatarImage));
                OnPropertyChanged(nameof(HasNoAvatarImage));
            }
        }
    }
    public bool HasAvatarImage => AvatarImage is not null;
    public bool HasNoAvatarImage => AvatarImage is null;
    public string ImageMessage
    {
        get => _imageMessage;
        private set => SetProperty(ref _imageMessage, value);
    }
    public void ReportImageError(string message) => ImageMessage = message;
    public ObservableCollection<RoomTileViewModel> Rooms { get; } = [];
    public ObservableCollection<RoomTileViewModel> RecentRooms { get; } = [];
    public ObservableCollection<RoomTileViewModel> ActiveRooms { get; } = [];
    public ObservableCollection<FriendViewModel> Friends { get; } = [];
    public ObservableCollection<ThemeChoiceViewModel> ThemeChoices { get; } = [];
    public string EditableUsername
    {
        get => _editableUsername;
        set => SetProperty(ref _editableUsername, value);
    }
    public string ProfileMessage
    {
        get => _profileMessage;
        private set => SetProperty(ref _profileMessage, value);
    }
    public string SelectedThemeName => AppTheme.Find(_selectedThemeId).Name;
    public ObservableCollection<RoomParticipantViewModel> Participants { get; } = [];
    public int ParticipantsCount => Participants.Count;
    public bool HasActiveSharer => Participants.Any(participant => participant.IsSharing);
    public bool HasNoActiveSharer => !HasActiveSharer;
    public WriteableBitmap? SharedScreenImage
    {
        get => _sharedScreenImage;
        private set
        {
            if (SetProperty(ref _sharedScreenImage, value))
            {
                OnPropertyChanged(nameof(HasSharedScreen));
                OnPropertyChanged(nameof(HasNoSharedScreen));
                NotifySharePresentationState();
                if (!HasSharedScreen)
                    IsVideoFullscreen = false;
                EnterVideoFullscreenCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public bool HasSharedScreen => SharedScreenImage is not null;
    public bool HasNoSharedScreen => SharedScreenImage is null;

    public bool IsVideoFullscreen
    {
        get => _isVideoFullscreen;
        private set
        {
            if (SetProperty(ref _isVideoFullscreen, value))
            {
                EnterVideoFullscreenCommand.NotifyCanExecuteChanged();
                ExitVideoFullscreenCommand.NotifyCanExecuteChanged();
            }
        }
    }
    public bool ShowLocalSharingStatus => IsSharing && !HasSharedScreen;
    public bool ShowWaitingForStream => !IsSharing && HasActiveSharer && !HasSharedScreen;
    public bool ShowIdleStreamPlaceholder => !HasSharedScreen && !IsSharing && !HasActiveSharer;
    public string ViewerMessage
    {
        get => _viewerMessage;
        private set => SetProperty(ref _viewerMessage, value);
    }
    public bool IsPublisherActive => _publisher is not null;
    public bool IsPublisherInactive => _publisher is null;
    public bool IsParticipantsSidebarVisible => IsRoomSelected && _isWideLayout;
    public bool IsParticipantsInlineVisible => IsRoomSelected && !_isWideLayout;
    public bool HasNoRooms => Rooms.Count == 0;
    public bool HasNoRecentRooms => RecentRooms.Count == 0;
    public bool HasNoActiveRooms => ActiveRooms.Count == 0;
    public bool HasNoFriends => Friends.Count == 0;
    public int RoomCount => Rooms.Count;
    public bool IsRoomSelected => SelectedRoom is not null && !IsProfilePage;
    public bool IsHome => SelectedRoom is null && !IsProfilePage;
    public bool IsProfilePage
    {
        get => _isProfilePage;
        private set
        {
            if (SetProperty(ref _isProfilePage, value))
            {
                OnPropertyChanged(nameof(IsRoomSelected));
                OnPropertyChanged(nameof(IsHome));
                OnPropertyChanged(nameof(IsParticipantsSidebarVisible));
                OnPropertyChanged(nameof(IsParticipantsInlineVisible));
                OnPropertyChanged(nameof(SelectedRoomWebUrl));
                StartSharingCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string FriendUsernameInput
    {
        get => _friendUsernameInput;
        set => SetProperty(ref _friendUsernameInput, value);
    }

    public string FriendMessage
    {
        get => _friendMessage;
        private set => SetProperty(ref _friendMessage, value);
    }

    public bool IsProfileOpen
    {
        get => _isProfileOpen;
        set => SetProperty(ref _isProfileOpen, value);
    }

    public bool IsRoomComposerOpen
    {
        get => _isRoomComposerOpen;
        set => SetProperty(ref _isRoomComposerOpen, value);
    }

    public double ComposerWidth
    {
        get => _composerWidth;
        private set => SetProperty(ref _composerWidth, value);
    }

    public double ComposerMaxHeight
    {
        get => _composerMaxHeight;
        private set => SetProperty(ref _composerMaxHeight, value);
    }

    public void SetViewportSize(double width, double height)
    {
        ComposerWidth = Math.Max(300, Math.Min(500, width - 48));
        ComposerMaxHeight = Math.Max(300, height - 48);
        var isWideLayout = width >= 700;
        if (_isWideLayout != isWideLayout)
        {
            _isWideLayout = isWideLayout;
            OnPropertyChanged(nameof(IsParticipantsSidebarVisible));
            OnPropertyChanged(nameof(IsParticipantsInlineVisible));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CreateRoomCommand.NotifyCanExecuteChanged();
                JoinRoomCommand.NotifyCanExecuteChanged();
                AddFriendCommand.NotifyCanExecuteChanged();
                SaveUsernameCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string NewRoomName
    {
        get => _newRoomName;
        set => SetProperty(ref _newRoomName, value);
    }

    public string InviteCodeInput
    {
        get => _inviteCodeInput;
        set => SetProperty(ref _inviteCodeInput, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public RoomTileViewModel? SelectedRoom
    {
        get => _selectedRoom;
        private set
        {
            if (SetProperty(ref _selectedRoom, value))
            {
                OnPropertyChanged(nameof(IsRoomSelected));
                OnPropertyChanged(nameof(IsHome));
                OnPropertyChanged(nameof(IsParticipantsSidebarVisible));
                OnPropertyChanged(nameof(IsParticipantsInlineVisible));
                foreach (var room in Rooms)
                    room.IsSelected = room == value;
                OnPropertyChanged(nameof(SelectedRoomWebUrl));
                StartSharingCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IRelayCommand OpenRoomComposerCommand { get; }
    public IRelayCommand ShowHomeCommand { get; }
    public IRelayCommand OpenWebRoomCommand { get; }
    public IAsyncRelayCommand StartSharingCommand { get; }
    public IAsyncRelayCommand StopSharingCommand { get; }
    public IRelayCommand OpenProfilePageCommand { get; }
    public IAsyncRelayCommand SaveUsernameCommand { get; }
    public IRelayCommand CloseRoomComposerCommand { get; }
    public IRelayCommand ToggleProfileCommand { get; }
    public IRelayCommand CloseProfileCommand { get; }
    public IRelayCommand LogoutCommand { get; }
    public IAsyncRelayCommand CreateRoomCommand { get; }
    public IAsyncRelayCommand JoinRoomCommand { get; }
    public IAsyncRelayCommand AddFriendCommand { get; }
    public IRelayCommand EnterVideoFullscreenCommand { get; }
    public IRelayCommand ExitVideoFullscreenCommand { get; }

    public async Task LoadRoomsAsync()
    {
        if (string.IsNullOrEmpty(AccessToken)) return;
        try
        {
            await LoadProfileAsync();
            using var request = AuthorizedRequest(HttpMethod.Get, "rooms");
            using var response = await Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            UpdateRooms(await response.Content.ReadFromJsonAsync<RoomResponse[]>() ?? []);
            await LoadFriendsAsync();
            await LoadAccountImageAsync();
            await LoadRoomImagesAsync();
            _refreshTimer.Start();
        }
        catch (HttpRequestException)
        {
            Message = "Não foi possível carregar suas salas. Confira a conexão com o servidor.";
        }
        catch (TaskCanceledException)
        {
            Message = "O servidor demorou para responder ao carregar as salas.";
        }
    }

    private async Task CreateRoomAsync()
    {
        var name = NewRoomName.Trim();
        if (name.Length is < 3 or > 80)
        {
            Message = "Use de 3 a 80 caracteres para o nome da sala.";
            return;
        }
        await SaveRoomAsync("rooms", new { name }, "Sala criada. Compartilhe o código com seus amigos.");
    }

    private async Task JoinRoomAsync()
    {
        var inviteCode = InviteCodeInput.Trim().ToUpperInvariant();
        if (inviteCode.Length != 10)
        {
            Message = "Informe um código de convite de 10 caracteres.";
            return;
        }
        await SaveRoomAsync("rooms/join", new { inviteCode }, "Você entrou na sala.");
    }

    private async Task SaveRoomAsync(string path, object body, string successMessage)
    {
        IsBusy = true;
        Message = string.Empty;
        try
        {
            using var request = AuthorizedRequest(HttpMethod.Post, path);
            request.Content = JsonContent.Create(body);
            using var response = await Client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Message = response.StatusCode switch
                {
                    HttpStatusCode.NotFound => "Sala não encontrada. Confira o código de convite.",
                    HttpStatusCode.Conflict => "Esta sala já tem seis participantes.",
                    HttpStatusCode.Unauthorized => "Sua sessão expirou. Entre novamente no Vento Sharing.",
                    _ => "Não foi possível salvar a sala. Confira os dados e tente novamente."
                };
                return;
            }

            var room = await response.Content.ReadFromJsonAsync<RoomResponse>();
            if (room is null)
            {
                Message = "O servidor não retornou os dados da sala.";
                return;
            }
            var existing = Rooms.FirstOrDefault(tile => tile.Id == room.Id);
            if (existing is not null)
                Rooms.Remove(existing);
            var tile = RoomTileViewModel.Room(room, SelectRoom);
            Rooms.Add(tile);
            OnPropertyChanged(nameof(HasNoRooms));
            OnPropertyChanged(nameof(RoomCount));
            SelectRoom(tile);
            IsRoomComposerOpen = false;
            NewRoomName = string.Empty;
            InviteCodeInput = string.Empty;
            Message = successMessage;
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
        }
    }

    private HttpRequestMessage AuthorizedRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(_server, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        return request;
    }

    private void OpenWebRoom()
    {
        if (SelectedRoom is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(SelectedRoomWebUrl) { UseShellExecute = true });
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShareMessage = "Não foi possível abrir o navegador. Copie o link da sala.";
        }
    }

    private void SetShareBusy(bool value)
    {
        _shareBusy = value;
        StartSharingCommand.NotifyCanExecuteChanged();
        StopSharingCommand.NotifyCanExecuteChanged();
    }

    private void NotifyPublisherChanged()
    {
        OnPropertyChanged(nameof(IsPublisherActive));
        OnPropertyChanged(nameof(IsPublisherInactive));
        StartSharingCommand.NotifyCanExecuteChanged();
        StopSharingCommand.NotifyCanExecuteChanged();
    }

    private async Task StartSharingAsync()
    {
        var room = SelectedRoom;
        if (room is null || _publisher is not null || _shareBusy) return;
        SetShareBusy(true);
        ShareMessage = "Preparando a transmissão…";
        NativePublisherSession? session = null;
        Guid? acquiredSessionId = null;
        try
        {
            using var request = AuthorizedRequest(HttpMethod.Post, $"rooms/{room.Id}/publisher-token");
            using var response = await Client.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                ShareMessage = "Outra pessoa já está iniciando ou compartilhando a tela nesta sala.";
                return;
            }
            response.EnsureSuccessStatusCode();
            var connection = await response.Content.ReadFromJsonAsync<MediaConnectionResponse>();
            if (connection is null || !connection.CanPublish || connection.SessionId is null)
                throw new InvalidOperationException("O servidor não autorizou a transmissão.");
            acquiredSessionId = connection.SessionId;
            if (SelectedRoom?.Id != room.Id) return;
            session = new NativePublisherSession(connection.Url, connection.Token);
            var currentSession = session;
            session.StateChanged += state => Dispatcher.UIThread.Post(() => _ = HandlePublisherStateAsync(currentSession, state));
            session.OutputProfileStatus += status => Dispatcher.UIThread.Post(() =>
            {
                if (_publisher != currentSession) return;
                ShareStreamProfile = status;
            });
            _publisher = session;
            ShareStreamProfile = string.Empty;
            NotifyPublisherChanged();
            _sharingRoomId = room.Id;
            _sharingSessionId = acquiredSessionId;
            ShareMessage = OperatingSystem.IsWindows()
                ? "Escolha uma tela ou janela no seletor do Windows."
                : "Escolha uma tela ou janela no seletor do sistema.";
            await session.StartAsync(_hostWindowHandle);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException
            or IOException or InvalidOperationException or PlatformNotSupportedException
            or System.ComponentModel.Win32Exception)
        {
            if (session is not null) await session.DisposeAsync();
            _publisher = null;
            NotifyPublisherChanged();
            _sharingRoomId = null;
            _sharingSessionId = null;
            ShareMessage = "Não foi possível iniciar. Confira a API, o LiveKit e a captura de tela do sistema.";
        }
        finally
        {
            if (_publisher is null && acquiredSessionId is Guid lease)
                await ReleaseBroadcastPresenceAsync(room.Id, lease);
            SetShareBusy(false);
        }
    }

    private async Task HandlePublisherStateAsync(NativePublisherSession session, string state)
    {
        if (_publisher != session) return;
        if (state is "pending" or "publishing")
        {
            if (state == "publishing") IsSharing = true;
            if (IsSharing) ShareMessage = "Tela sendo transmitida. O áudio está desligado.";
            if (!await RenewBroadcastPresenceAsync(IsSharing))
            {
                await StopSharingAsync();
                ShareMessage = "A reserva da transmissão expirou. Inicie novamente.";
                return;
            }
            if (_sharingRoomId is Guid roomId && SelectedRoom?.Id == roomId)
                await LoadParticipantsAsync(roomId);
        }
        else if (state == "failed")
        {
            await StopSharingAsync();
            ShareMessage = session.ErrorMessage.Contains("cancelada", StringComparison.OrdinalIgnoreCase)
                ? "Seleção de tela cancelada."
                : "A captura nativa falhou. Confira o portal de tela e o PipeWire.";
        }
    }

    public async Task StopSharingAsync()
    {
        if (_publisher is null || _shareBusy) return;
        SetShareBusy(true);
        var session = _publisher;
        var roomId = _sharingRoomId;
        var lease = _sharingSessionId;
        _publisher = null;
        NotifyPublisherChanged();
        IsSharing = false;
        ShareStreamProfile = string.Empty;
        ClearSharedScreen();
        ViewerMessage = string.Empty;
        try
        {
            await Task.Run(async () => await session.DisposeAsync());
            if (roomId is Guid sharedRoomId && lease is Guid sessionId)
                await ReleaseBroadcastPresenceAsync(sharedRoomId, sessionId);
            if (roomId is Guid activeRoomId && SelectedRoom?.Id == activeRoomId)
                await LoadParticipantsAsync(activeRoomId);
            _sharingRoomId = null;
            _sharingSessionId = null;
            ShareMessage = "Transmissão encerrada.";
        }
        finally { SetShareBusy(false); }
    }

    private async Task<bool> RenewBroadcastPresenceAsync(bool publishing)
    {
        var roomId = _sharingRoomId;
        var sessionId = _sharingSessionId;
        if (roomId is null || sessionId is null) return false;
        try
        {
            using var request = AuthorizedRequest(HttpMethod.Post,
                $"rooms/{roomId}/broadcast/presence");
            request.Content = JsonContent.Create(new { sessionId, isPublishing = publishing });
            using var response = await Client.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException) { return false; }
    }

    private async Task ReleaseBroadcastPresenceAsync(Guid roomId, Guid sessionId)
    {
        try
        {
            using var request = AuthorizedRequest(HttpMethod.Delete, $"rooms/{roomId}/broadcast/presence");
            request.Content = JsonContent.Create(new { sessionId, isPublishing = false });
            using var response = await Client.SendAsync(request);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException) { }
    }

    private void StopRoomViewer()
    {
        ++_viewerGeneration;
        var previous = _viewer;
        _viewer = null;
        ClearSharedScreen();
        if (previous is not null) _ = previous.DisposeAsync();
    }

    private async Task StartRoomViewerAsync(Guid roomId)
    {
        StopRoomViewer();
        var generation = _viewerGeneration;
        DesktopRoomViewer? viewer = null;
        ViewerMessage = string.Empty;
        try
        {
            using var request = AuthorizedRequest(HttpMethod.Post, $"rooms/{roomId}/media-token");
            using var response = await Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var connection = await response.Content.ReadFromJsonAsync<MediaConnectionResponse>();
            if (connection is null || connection.CanPublish)
                throw new InvalidOperationException("Token de visualização inválido.");
            if (generation != _viewerGeneration || SelectedRoom?.Id != roomId) return;

            viewer = new DesktopRoomViewer(connection.Url, connection.Token);
            var currentViewer = viewer;
            viewer.FrameReceived += (width, height, pixels) =>
                Dispatcher.UIThread.Post(() => ShowViewerFrame(currentViewer, width, height, pixels));
            _viewer = viewer;
            await viewer.StartAsync();
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException
            or InvalidOperationException or IOException)
        {
            var remainsSelected = generation == _viewerGeneration && SelectedRoom?.Id == roomId;
            if (viewer is not null && _viewer == viewer) StopRoomViewer();
            if (remainsSelected)
                ViewerMessage = "Não foi possível carregar o vídeo da sala.";
        }
    }

    private void ShowViewerFrame(DesktopRoomViewer viewer, int width, int height, byte[]? pixels)
    {
        if (_viewer != viewer) return;
        if (pixels is null)
        {
            ClearSharedScreen();
            return;
        }
        if (!HasActiveSharer && !IsSharing)
        {
            ClearSharedScreen();
            return;
        }
        if (width is < 1 or > 3840 || height is < 1 or > 2160 || pixels.Length < width * height * 4)
            return;
        if (SharedScreenImage is null || width != _screenWidth || height != _screenHeight)
        {
            ClearSharedScreen();
            _screenWidth = width;
            _screenHeight = height;
            SharedScreenImage = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }
        using (var framebuffer = SharedScreenImage!.Lock())
        {
            var rowBytes = width * 4;
            for (var row = 0; row < height; row++)
                Marshal.Copy(pixels, row * rowBytes, IntPtr.Add(framebuffer.Address, row * framebuffer.RowBytes), rowBytes);
        }
        OnPropertyChanged(nameof(SharedScreenImage));
    }

    private void ClearSharedScreen()
    {
        var previous = SharedScreenImage;
        SharedScreenImage = null;
        _screenWidth = 0;
        _screenHeight = 0;
        previous?.Dispose();
    }

    private void SelectRoom(RoomTileViewModel room)
    {
        if (_sharingRoomId.HasValue && _sharingRoomId != room.Id)
            _ = StopSharingAsync();
        if (SelectedRoom is not null && SelectedRoom.Id != room.Id)
            _ = LeaveRoomAsync(SelectedRoom.Id);
        SelectedRoom = room;
        IsProfilePage = false;
        IsProfileOpen = false;
        IsRoomComposerOpen = false;
        Message = string.Empty;
        room.LastVisitedAt = DateTimeOffset.UtcNow;
        RebuildHomeRooms();
        Participants.Clear();
        NotifyParticipantsChanged();
        _ = LoadParticipantsAsync(room.Id);
        _ = StartRoomViewerAsync(room.Id);
        _ = RefreshRoomsAsync();
    }

    private void ShowHome()
    {
        _ = StopSharingAsync();
        StopRoomViewer();
        var previousRoomId = SelectedRoom?.Id;
        SelectedRoom = null;
        IsProfilePage = false;
        Participants.Clear();
        NotifyParticipantsChanged();
        IsProfileOpen = false;
        IsRoomComposerOpen = false;
        Message = string.Empty;
        _ = ReturnHomeAsync(previousRoomId);
    }

    private void OpenProfilePage()
    {
        _ = StopSharingAsync();
        StopRoomViewer();
        var previousRoomId = SelectedRoom?.Id;
        SelectedRoom = null;
        IsProfilePage = true;
        IsProfileOpen = false;
        IsRoomComposerOpen = false;
        ProfileMessage = string.Empty;
        ImageMessage = string.Empty;
        if (previousRoomId.HasValue)
            _ = LeaveRoomAsync(previousRoomId.Value);
    }

    private async Task LoadProfileAsync()
    {
        using var request = AuthorizedRequest(HttpMethod.Get, "account/profile");
        using var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var profile = await response.Content.ReadFromJsonAsync<AccountProfileResponse>();
        if (profile is null) return;
        Username = profile.Username;
        EditableUsername = profile.Username;
        SetTheme(profile.ThemeId);
    }

    private async Task SaveUsernameAsync()
    {
        var proposed = EditableUsername.Trim();
        if (proposed.Length is < 3 or > 32 || !char.IsLetterOrDigit(proposed[0])
            || proposed.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not ('.' or '_' or '-')))
        {
            ProfileMessage = "Use 3 a 32 caracteres: letras, números, ponto, _ ou -.";
            return;
        }
        IsBusy = true;
        ProfileMessage = string.Empty;
        try
        {
            using var request = AuthorizedRequest(HttpMethod.Put, "account/profile/username");
            request.Content = JsonContent.Create(new { username = proposed });
            using var response = await Client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                ProfileMessage = response.StatusCode == HttpStatusCode.BadRequest
                    ? "Esse username já está em uso ou é inválido."
                    : "Não foi possível atualizar o username.";
                return;
            }
            var previous = Username;
            Username = proposed;
            EditableUsername = proposed;
            ProfileMessage = "Username atualizado.";
            try
            {
                var saved = SavedCredentialsStore.Load();
                if (saved is not null && string.Equals(saved.Identifier, previous, StringComparison.OrdinalIgnoreCase))
                    SavedCredentialsStore.Save(saved with { Identifier = proposed });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
            {
                ProfileMessage = "Username atualizado. Atualize o login salvo neste computador.";
            }
        }
        catch (HttpRequestException) { ProfileMessage = "Não foi possível conectar ao servidor."; }
        catch (TaskCanceledException) { ProfileMessage = "O servidor demorou para responder."; }
        finally { IsBusy = false; }
    }

    private async Task SelectThemeAsync(string themeId)
    {
        if (_isSavingTheme || themeId == _selectedThemeId) return;
        _isSavingTheme = true;
        var previous = _selectedThemeId;
        SetTheme(themeId);
        ProfileMessage = string.Empty;
        try
        {
            using var request = AuthorizedRequest(HttpMethod.Put, "account/profile/theme");
            request.Content = JsonContent.Create(new { themeId });
            using var response = await Client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                SetTheme(previous);
                ProfileMessage = "Não foi possível salvar a cor escolhida.";
                return;
            }
            ProfileMessage = "Cor do layout salva na sua conta.";
        }
        catch (HttpRequestException)
        {
            SetTheme(previous);
            ProfileMessage = "Não foi possível salvar a cor escolhida.";
        }
        catch (TaskCanceledException)
        {
            SetTheme(previous);
            ProfileMessage = "O servidor demorou para salvar a cor.";
        }
        finally { _isSavingTheme = false; }
    }

    private void SetTheme(string? themeId)
    {
        _selectedThemeId = AppTheme.Find(themeId).Id;
        AppTheme.Apply(_selectedThemeId);
        OnPropertyChanged(nameof(SelectedThemeName));
        foreach (var choice in ThemeChoices)
            choice.IsSelected = choice.Id == _selectedThemeId;
    }

    private async Task ReturnHomeAsync(Guid? previousRoomId)
    {
        if (previousRoomId.HasValue)
            await LeaveRoomAsync(previousRoomId.Value);
        await RefreshRoomsAsync();
    }

    private void UpdateRooms(RoomResponse[] responses)
    {
        var selectedId = SelectedRoom?.Id;
        var ids = responses.Select(room => room.Id).ToHashSet();
        foreach (var old in Rooms.Where(room => !ids.Contains(room.Id)).ToArray())
            Rooms.Remove(old);
        foreach (var response in responses.OrderBy(room => room.Name))
        {
            var tile = Rooms.FirstOrDefault(room => room.Id == response.Id);
            if (tile is null)
                Rooms.Add(RoomTileViewModel.Room(response, SelectRoom));
            else
                tile.Update(response);
        }
        SelectedRoom = selectedId is null ? null : Rooms.FirstOrDefault(room => room.Id == selectedId);
        if (_sharingRoomId.HasValue && SelectedRoom?.Id != _sharingRoomId)
            _ = StopSharingAsync();
        if (SelectedRoom is null && Participants.Count > 0)
        {
            Participants.Clear();
            NotifyParticipantsChanged();
        }
        RebuildHomeRooms();
        OnPropertyChanged(nameof(HasNoRooms));
        OnPropertyChanged(nameof(RoomCount));
    }

    private void RebuildHomeRooms()
    {
        RecentRooms.Clear();
        foreach (var room in Rooms.Where(room => room.LastVisitedAt.HasValue)
                     .OrderByDescending(room => room.LastVisitedAt).Take(4))
            RecentRooms.Add(room);
        ActiveRooms.Clear();
        foreach (var room in Rooms.Where(room => room.OnlineCount > 0)
                     .OrderByDescending(room => room.OnlineCount))
            ActiveRooms.Add(room);
        OnPropertyChanged(nameof(HasNoRecentRooms));
        OnPropertyChanged(nameof(HasNoActiveRooms));
    }

    private async Task RefreshRoomsAsync()
    {
        if (_isRefreshing || string.IsNullOrEmpty(AccessToken)) return;
        _isRefreshing = true;
        try
        {
            if (SelectedRoom is not null)
            {
                using var presence = AuthorizedRequest(HttpMethod.Post, $"rooms/{SelectedRoom.Id}/presence");
                using var presenceResponse = await Client.SendAsync(presence);
                presenceResponse.EnsureSuccessStatusCode();
            }
            using var request = AuthorizedRequest(HttpMethod.Get, "rooms");
            using var response = await Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            UpdateRooms(await response.Content.ReadFromJsonAsync<RoomResponse[]>() ?? []);
            if (SelectedRoom is not null)
                await LoadParticipantsAsync(SelectedRoom.Id);
            await LoadRoomImagesAsync();
        }
        catch (HttpRequestException)
        {
            Message = "Não foi possível atualizar as salas.";
        }
        catch (TaskCanceledException)
        {
            Message = "O servidor demorou para atualizar as salas.";
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async Task LeaveRoomAsync(Guid roomId)
    {
        try
        {
            using var request = AuthorizedRequest(HttpMethod.Delete, $"rooms/{roomId}/presence");
            using var response = await Client.SendAsync(request);
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
    }

    private async Task LoadParticipantsAsync(Guid roomId)
    {
        try
        {
            using var request = AuthorizedRequest(HttpMethod.Get, $"rooms/{roomId}/participants");
            using var response = await Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var members = await response.Content.ReadFromJsonAsync<RoomParticipantResponse[]>() ?? [];
            if (SelectedRoom?.Id != roomId) return;
            Participants.Clear();
            foreach (var member in members)
                Participants.Add(new RoomParticipantViewModel(member.Username, member.IsOwner,
                    member.IsOnline, member.IsSharing));
            NotifyParticipantsChanged();
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
    }

    private void NotifyParticipantsChanged()
    {
        OnPropertyChanged(nameof(ParticipantsCount));
        OnPropertyChanged(nameof(HasActiveSharer));
        OnPropertyChanged(nameof(HasNoActiveSharer));
        NotifySharePresentationState();
    }

    private void NotifySharePresentationState()
    {
        OnPropertyChanged(nameof(ShowLocalSharingStatus));
        OnPropertyChanged(nameof(ShowWaitingForStream));
        OnPropertyChanged(nameof(ShowIdleStreamPlaceholder));
    }

    private void EnterVideoFullscreen()
    {
        if (!HasSharedScreen)
            return;
        IsProfileOpen = false;
        IsRoomComposerOpen = false;
        IsVideoFullscreen = true;
    }

    public void ExitVideoFullscreen() => IsVideoFullscreen = false;

    private async Task LoadFriendsAsync()
    {
        try
        {
            using var request = AuthorizedRequest(HttpMethod.Get, "friends");
            using var response = await Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var friends = await response.Content.ReadFromJsonAsync<FriendResponse[]>() ?? [];
            Friends.Clear();
            foreach (var friend in friends)
                Friends.Add(new FriendViewModel(friend.Username));
            OnPropertyChanged(nameof(HasNoFriends));
        }
        catch (HttpRequestException)
        {
            FriendMessage = "Não foi possível carregar seus amigos.";
        }
        catch (TaskCanceledException)
        {
            FriendMessage = "O servidor demorou para carregar seus amigos.";
        }
    }

    private async Task AddFriendAsync()
    {
        var username = FriendUsernameInput.Trim();
        if (username.Length is < 3 or > 32)
        {
            FriendMessage = "Informe um username de 3 a 32 caracteres.";
            return;
        }
        IsBusy = true;
        FriendMessage = string.Empty;
        try
        {
            using var request = AuthorizedRequest(HttpMethod.Post, "friends");
            request.Content = JsonContent.Create(new { username });
            using var response = await Client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                FriendMessage = response.StatusCode == HttpStatusCode.NotFound
                    ? "Usuário não encontrado. Confira o username."
                    : "Não foi possível adicionar este amigo.";
                return;
            }
            FriendUsernameInput = string.Empty;
            await LoadFriendsAsync();
            FriendMessage = "Amigo adicionado.";
        }
        catch (HttpRequestException)
        {
            FriendMessage = "Não foi possível conectar ao servidor Vento.";
        }
        catch (TaskCanceledException)
        {
            FriendMessage = "O servidor demorou para responder.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UploadAccountImageAsync(Stream source, string contentType)
    {
        await UploadImageAsync("account/image", source, contentType, async () =>
        {
            await LoadAccountImageAsync();
            if (AvatarImage is not null)
                ImageMessage = "Foto do perfil atualizada.";
        });
    }

    public async Task UploadRoomImageAsync(Stream source, string contentType)
    {
        var room = SelectedRoom;
        if (room is null || !room.IsOwner) return;
        await UploadImageAsync($"rooms/{room.Id}/image", source, contentType, async () =>
        {
            room.ImageUpdatedAt = DateTimeOffset.UtcNow;
            await LoadRoomImageAsync(room);
            if (room.Image is not null)
                ImageMessage = "Imagem da sala atualizada.";
        });
    }

    public async Task RemoveAccountImageAsync()
    {
        await DeleteImageAsync("account/image", () =>
        {
            AvatarImage = null;
            ImageMessage = "Foto do perfil removida.";
        });
    }

    public async Task RemoveRoomImageAsync()
    {
        var room = SelectedRoom;
        if (room is null || !room.IsOwner) return;
        await DeleteImageAsync($"rooms/{room.Id}/image", () =>
        {
            room.ImageUpdatedAt = null;
            room.Image = null;
            ImageMessage = "Imagem da sala removida.";
        });
    }

    private async Task UploadImageAsync(string path, Stream source, string contentType, Func<Task> onSuccess)
    {
        ImageMessage = string.Empty;
        try
        {
            await using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await source.ReadAsync(chunk)) > 0)
            {
                if (buffer.Length + read > 2 * 1024 * 1024)
                {
                    ImageMessage = "Use uma imagem de até 2 MB.";
                    return;
                }
                await buffer.WriteAsync(chunk.AsMemory(0, read));
            }
            using var request = AuthorizedRequest(HttpMethod.Put, path);
            request.Content = new ByteArrayContent(buffer.ToArray());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            using var response = await Client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                ImageMessage = response.StatusCode switch
                {
                    HttpStatusCode.RequestEntityTooLarge => "Use uma imagem de até 2 MB.",
                    HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType =>
                        "Use PNG ou JPEG com até 2048 × 2048 pixels.",
                    HttpStatusCode.Forbidden => "Só o anfitrião pode alterar a imagem da sala.",
                    _ => "Não foi possível enviar a imagem."
                };
                return;
            }
            await onSuccess();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException)
        {
            ImageMessage = "Não foi possível ler ou enviar a imagem.";
        }
    }

    private async Task DeleteImageAsync(string path, Action onSuccess)
    {
        ImageMessage = string.Empty;
        try
        {
            using var request = AuthorizedRequest(HttpMethod.Delete, path);
            using var response = await Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            onSuccess();
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            ImageMessage = "Não foi possível remover a imagem.";
        }
    }

    private async Task LoadAccountImageAsync()
    {
        try
        {
            using var request = AuthorizedRequest(HttpMethod.Get, "account/image");
            using var response = await Client.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                AvatarImage = null;
                return;
            }
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            AvatarImage = new Bitmap(stream);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException
            or InvalidOperationException or ArgumentException or InvalidDataException or NotSupportedException)
        {
            ImageMessage = "Não foi possível carregar a foto do perfil.";
        }
    }

    private async Task LoadRoomImagesAsync()
    {
        foreach (var room in Rooms.Where(room => room.ImageUpdatedAt.HasValue && room.Image is null).ToArray())
            await LoadRoomImageAsync(room);
    }

    private async Task LoadRoomImageAsync(RoomTileViewModel room)
    {
        try
        {
            using var request = AuthorizedRequest(HttpMethod.Get, $"rooms/{room.Id}/image");
            using var response = await Client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;
            await using var stream = await response.Content.ReadAsStreamAsync();
            room.Image = new Bitmap(stream);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException
            or InvalidOperationException or ArgumentException or InvalidDataException or NotSupportedException) { }
    }
}

public sealed record RoomResponse(Guid Id, string Name, string InviteCode, bool IsOwner,
    int ParticipantCount, int OnlineCount, DateTimeOffset? LastVisitedAt,
    DateTimeOffset? ImageUpdatedAt);
public sealed record AccountProfileResponse(string Username, string Email, string ThemeId);
public sealed record FriendResponse(string Username);
public sealed class ThemeChoiceViewModel : ViewModelBase
{
    private bool _isSelected;

    public ThemeChoiceViewModel(ThemePalette palette, Action select)
    {
        Id = palette.Id;
        Name = palette.Name;
        PreviewBrush = Brush.Parse(palette.Accent);
        SelectCommand = new RelayCommand(select);
    }

    public string Id { get; }
    public string Name { get; }
    public IBrush PreviewBrush { get; }
    public IRelayCommand SelectCommand { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
public sealed record RoomParticipantResponse(string Username, bool IsOwner, bool IsOnline, bool IsSharing);
public sealed record MediaConnectionResponse(string Url, string Token, bool CanPublish, Guid? SessionId);
public sealed record RoomParticipantViewModel(string Username, bool IsOwner, bool IsOnline, bool IsSharing)
{
    public string Initial => Username.Length > 0 ? Username[..1].ToUpperInvariant() : "V";
    public string Status => IsSharing ? "Compartilhando" : IsOnline ? "Conectado" : "Offline";
}
public sealed record FriendViewModel(string Username)
{
    public string Initial => Username.Length > 0 ? Username[..1].ToUpperInvariant() : "V";
}

public sealed class RoomTileViewModel : ViewModelBase
{
    private bool _isSelected;
    private Bitmap? _image;
    private DateTimeOffset? _imageUpdatedAt;

    private RoomTileViewModel(Guid id, string title, string inviteCode,
        string role, int participantCount, Action<RoomTileViewModel> onSelect)
    {
        Id = id;
        Title = title;
        Initial = title.Length > 0 ? title[..1].ToUpperInvariant() : "S";
        InviteCode = inviteCode;
        Role = role;
        ParticipantCount = participantCount;
        OpenCommand = new RelayCommand(() => onSelect(this));
    }

    public Guid Id { get; }
    public string Title { get; }
    public string Initial { get; }
    public string InviteCode { get; }
    public string Role { get; }
    public bool IsOwner { get; private set; }
    public int ParticipantCount { get; private set; }
    public int OnlineCount { get; private set; }
    public DateTimeOffset? LastVisitedAt { get; set; }
    public DateTimeOffset? ImageUpdatedAt
    {
        get => _imageUpdatedAt;
        set
        {
            if (SetProperty(ref _imageUpdatedAt, value))
                Image = null;
        }
    }
    public Bitmap? Image
    {
        get => _image;
        set
        {
            if (SetProperty(ref _image, value))
            {
                OnPropertyChanged(nameof(HasImage));
                OnPropertyChanged(nameof(HasNoImage));
                OnPropertyChanged(nameof(CanRemoveImage));
            }
        }
    }
    public bool HasImage => Image is not null;
    public bool HasNoImage => Image is null;
    public bool CanRemoveImage => IsOwner && HasImage;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
    public IRelayCommand OpenCommand { get; }

    public static RoomTileViewModel Room(RoomResponse room, Action<RoomTileViewModel> onSelect) =>
        new(room.Id, room.Name, room.InviteCode,
            room.IsOwner ? "Anfitrião" : "Participante", room.ParticipantCount, onSelect)
        {
            IsOwner = room.IsOwner,
            OnlineCount = room.OnlineCount,
            LastVisitedAt = room.LastVisitedAt,
            ImageUpdatedAt = room.ImageUpdatedAt
        };

    public void Update(RoomResponse room)
    {
        ParticipantCount = room.ParticipantCount;
        OnlineCount = room.OnlineCount;
        LastVisitedAt = room.LastVisitedAt;
        IsOwner = room.IsOwner;
        ImageUpdatedAt = room.ImageUpdatedAt;
        OnPropertyChanged(nameof(ParticipantCount));
        OnPropertyChanged(nameof(OnlineCount));
        OnPropertyChanged(nameof(LastVisitedAt));
        OnPropertyChanged(nameof(IsOwner));
        OnPropertyChanged(nameof(CanRemoveImage));
    }
}
