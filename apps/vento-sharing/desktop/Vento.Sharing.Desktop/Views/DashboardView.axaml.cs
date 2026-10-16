using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Vento.Sharing.Desktop.ViewModels;

namespace Vento.Sharing.Desktop.Views;

public partial class DashboardView : UserControl
{
    private MainViewModel? _observedViewModel;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.SharedScreenImage))
            SharedVideoImage.InvalidateVisual();
    }

    private async void OnCopyWebRoomLinkClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not MainViewModel viewModel || string.IsNullOrEmpty(viewModel.SelectedRoomWebUrl))
            return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            viewModel.ReportRoomMessage("A área de transferência não está disponível.");
            return;
        }
        await clipboard.SetTextAsync(viewModel.SelectedRoomWebUrl);
        viewModel.ReportRoomMessage("Link copiado. O convidado entrará na sala após fazer login na conta Vento.");
    }

    private async void OnUploadAccountImageClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is MainViewModel viewModel)
            await PickAndUploadImageAsync(viewModel, viewModel.UploadAccountImageAsync);
    }

    private async void OnUploadRoomImageClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is MainViewModel viewModel)
            await PickAndUploadImageAsync(viewModel, viewModel.UploadRoomImageAsync);
    }

    private async void OnRemoveAccountImageClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is MainViewModel viewModel)
            await viewModel.RemoveAccountImageAsync();
    }

    private async void OnRemoveRoomImageClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is MainViewModel viewModel)
            await viewModel.RemoveRoomImageAsync();
    }

    private async Task PickAndUploadImageAsync(MainViewModel viewModel, Func<Stream, string, Task> upload)
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null)
        {
            viewModel.ReportImageError("O seletor de arquivos não está disponível.");
            return;
        }
        try
        {
            var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Escolher imagem",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Imagens PNG e JPEG")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg"],
                        MimeTypes = ["image/png", "image/jpeg"]
                    }
                ]
            });
            if (files.Count == 0) return;
            var file = files[0];
            var extension = Path.GetExtension(file.Name);
            var contentType = extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png"
                : extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                  || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : null;
            if (contentType is null)
            {
                viewModel.ReportImageError("Escolha um arquivo PNG ou JPEG.");
                return;
            }
            await using var stream = await file.OpenReadAsync();
            await upload(stream, contentType);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            viewModel.ReportImageError("Não foi possível abrir a imagem escolhida.");
        }
    }

    private void OnProfileDismissLayerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.IsProfileOpen = false;

        args.Handled = true;
    }

    private void OnRoomComposerBackdropPressed(object? sender, PointerPressedEventArgs args)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.IsRoomComposerOpen = false;

        args.Handled = true;
    }

    public DashboardView()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateMainContentScroll();
        LayoutUpdated += (_, _) => UpdateMainContentScroll();
        DataContextChanged += (_, _) =>
        {
            if (_observedViewModel is not null)
                _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _observedViewModel = DataContext as MainViewModel;
            if (_observedViewModel is not null)
                _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        };
        RoomNameInput.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter || DataContext is not MainViewModel viewModel)
                return;

            viewModel.NewRoomName = RoomNameInput.Text ?? string.Empty;
            if (viewModel.CreateRoomCommand.CanExecute(null))
                viewModel.CreateRoomCommand.Execute(null);
            args.Handled = true;
        };
        InviteCodeInput.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter || DataContext is not MainViewModel viewModel)
                return;

            viewModel.InviteCodeInput = InviteCodeInput.Text ?? string.Empty;
            if (viewModel.JoinRoomCommand.CanExecute(null))
                viewModel.JoinRoomCommand.Execute(null);
            args.Handled = true;
        };
        FriendUsernameInput.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter || DataContext is not MainViewModel viewModel)
                return;

            viewModel.FriendUsernameInput = FriendUsernameInput.Text ?? string.Empty;
            if (viewModel.AddFriendCommand.CanExecute(null))
                viewModel.AddFriendCommand.Execute(null);
            args.Handled = true;
        };
        ProfileUsernameInput.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter || DataContext is not MainViewModel viewModel)
                return;

            viewModel.EditableUsername = ProfileUsernameInput.Text ?? string.Empty;
            if (viewModel.SaveUsernameCommand.CanExecute(null))
                viewModel.SaveUsernameCommand.Execute(null);
            args.Handled = true;
        };
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape || DataContext is not MainViewModel { IsRoomComposerOpen: true } viewModel)
                return;

            viewModel.IsRoomComposerOpen = false;
            args.Handled = true;
        };
        RoomComposerModal.PropertyChanged += (_, args) =>
        {
            if (args.Property.Name == "IsVisible" && RoomComposerModal.IsVisible)
                Dispatcher.UIThread.Post(() => RoomNameInput.Focus());
        };
    }

    private void UpdateMainContentScroll()
    {
        if (MainContentScroll is null || MainContentPanel is null)
            return;

        var viewport = MainContentScroll.Viewport.Height;
        if (viewport <= 0)
        {
            MainContentScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            return;
        }

        MainContentScroll.VerticalScrollBarVisibility =
            MainContentPanel.Bounds.Height > viewport + 1
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled;
    }
}
