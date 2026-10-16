#if WINDOWS_PUBLISHER
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Vento.Sharing.Desktop.Media;

internal static class WindowsPublisherCapture
{
    [ComImport]
    [Guid("3E68D4BD-7135-4D10-8018-9FB6D9F0FA02")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeWithWindow
    {
        void Initialize(IntPtr hwnd);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        void GetInterface(ref Guid iid, out IntPtr p);
    }

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    public static async Task<GraphicsCaptureItem?> PickCaptureItemAsync(IntPtr hostWindowHandle)
    {
        if (hostWindowHandle == IntPtr.Zero)
            throw new InvalidOperationException("Não foi possível associar a janela do aplicativo ao seletor de captura.");

        if (!GraphicsCaptureSession.IsSupported())
            throw new PlatformNotSupportedException("A captura de tela não é suportada nesta versão do Windows.");

        var picker = new GraphicsCapturePicker();
        ((IInitializeWithWindow)(object)picker).Initialize(hostWindowHandle);
        return await picker.PickSingleItemAsync();
    }

    public static Task RunAsync(
        GraphicsCaptureItem item,
        string serverUrl,
        string token,
        CancellationToken cancellationToken,
        Action<string> onState,
        Action onPublishing,
        Action<string> onFailed,
        Action<string>? onProfileStatus)
    {
        return Task.Run(() => PublishLoopAsync(item, serverUrl, token, cancellationToken, onState, onPublishing, onFailed, onProfileStatus),
            cancellationToken);
    }

    private static async Task PublishLoopAsync(
        GraphicsCaptureItem item,
        string serverUrl,
        string token,
        CancellationToken cancellationToken,
        Action<string> onState,
        Action onPublishing,
        Action<string> onFailed,
        Action<string>? onProfileStatus)
    {
        PublisherScreenShareSession? publisher = null;
        IDirect3DDevice? winrtDevice = null;
        ID3D11Device? d3dDevice = null;
        ID3D11DeviceContext? context = null;
        Direct3D11CaptureFramePool? framePool = null;
        GraphicsCaptureSession? captureSession = null;
        ID3D11Texture2D? staging = null;
        var publishing = false;
        var frameLock = new object();

        try
        {
            onState("pending");
            D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                null,
                out d3dDevice,
                out _,
                out context).CheckError();

            winrtDevice = CreateWinRtDevice(d3dDevice);
            var itemSize = item.Size;
            framePool = Direct3D11CaptureFramePool.Create(
                winrtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                itemSize);
            captureSession = framePool.CreateCaptureSession(item);
            captureSession.IsCursorCaptureEnabled = true;
            captureSession.StartCapture();

            publisher = new PublisherScreenShareSession();
            if (onProfileStatus is not null)
                publisher.OutputProfileStatus += onProfileStatus;
            await publisher.ConnectAsync(serverUrl, token, cancellationToken);

            framePool.FrameArrived += (_, _) =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                lock (frameLock)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;
                    using var frame = framePool.TryGetNextFrame();
                    if (frame is null)
                        return;

                    using var surface = frame.Surface;
                    var access = surface.As<IDirect3DDxgiInterfaceAccess>();
                    var textureGuid = typeof(ID3D11Texture2D).GUID;
                    access.GetInterface(ref textureGuid, out var texturePointer);
                    using var texture = new ID3D11Texture2D(texturePointer);
                    var desc = texture.Description;

                    staging ??= CreateStagingTexture(d3dDevice!, desc.Width, desc.Height);
                    if (staging.Description.Width != desc.Width || staging.Description.Height != desc.Height)
                    {
                        staging.Dispose();
                        staging = CreateStagingTexture(d3dDevice!, desc.Width, desc.Height);
                    }

                    context!.CopyResource(staging, texture);
                    var mapped = context.Map(staging, 0, MapMode.Read);
                    try
                    {
                        var published = publisher!.TryPublishBgraAsync(
                            mapped.DataPointer,
                            desc.Width,
                            desc.Height,
                            mapped.RowPitch,
                            cancellationToken).GetAwaiter().GetResult();
                        if (published && !publishing)
                        {
                            publishing = true;
                            onPublishing();
                        }
                    }
                    finally
                    {
                        context.Unmap(staging, 0);
                    }
                }
            };

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
        catch (Exception error) when (error is not OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            onFailed(error.Message);
            onState("failed");
        }
        finally
        {
            captureSession?.Dispose();
            framePool?.Dispose();
            staging?.Dispose();
            if (publisher is not null)
                await publisher.DisposeAsync();
            winrtDevice?.Dispose();
            context?.Dispose();
            d3dDevice?.Dispose();
        }
    }

    private static ID3D11Texture2D CreateStagingTexture(ID3D11Device device, int width, int height)
    {
        return device.CreateTexture2D(new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None
        });
    }

    private static IDirect3DDevice CreateWinRtDevice(ID3D11Device d3dDevice)
    {
        using var dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>();
        var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var winrtPointer);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
        return MarshalInterface<IDirect3DDevice>.FromAbi(winrtPointer);
    }
}
#endif
