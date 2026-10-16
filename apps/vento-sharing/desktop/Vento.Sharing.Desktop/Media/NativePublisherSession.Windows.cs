#if WINDOWS_PUBLISHER
using System;
using System.Threading.Tasks;

namespace Vento.Sharing.Desktop.Media;

public sealed partial class NativePublisherSession
{
    private async Task StartWindowsAsync(IntPtr hostWindowHandle)
    {
        StateChanged?.Invoke("pending");
        _heartbeat = ReportPresenceAsync(_lifetime.Token);
        try
        {
            var item = await WindowsPublisherCapture.PickCaptureItemAsync(hostWindowHandle);
            if (item is null)
            {
                ErrorMessage = "Nenhuma tela ou janela foi selecionada.";
                StateChanged?.Invoke("failed");
                return;
            }

            _worker = WindowsPublisherCapture.RunAsync(
                item,
                _serverUrl,
                _token,
                _lifetime.Token,
                state => StateChanged?.Invoke(state),
                () =>
                {
                    _isPublishing = true;
                    StateChanged?.Invoke("publishing");
                },
                message => ErrorMessage = message,
                status => OutputProfileStatus?.Invoke(status));
        }
        catch (Exception error) when (error is not OperationCanceledException && !_lifetime.IsCancellationRequested)
        {
            ErrorMessage = error.Message;
            StateChanged?.Invoke("failed");
        }
    }
}
#endif
