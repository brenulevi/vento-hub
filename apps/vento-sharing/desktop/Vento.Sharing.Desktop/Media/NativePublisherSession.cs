using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Proto;
using LiveKit.Rtc;

namespace Vento.Sharing.Desktop.Media;

/// <summary>Captures via the Linux screen cast portal and publishes from this process.</summary>
public sealed class NativePublisherSession : IAsyncDisposable
{
    private readonly string _serverUrl;
    private readonly string _token;
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _capture;
    private Task? _worker;
    private Task? _heartbeat;
    private volatile bool _isPublishing;
    private int _closed;

    public event Action<string>? StateChanged;
    public string ErrorMessage { get; private set; } = string.Empty;

    public NativePublisherSession(string serverUrl, string token)
    {
        _serverUrl = serverUrl;
        _token = token;
    }

    public void Start()
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("A captura nativa está disponível no Linux nesta etapa.");
        var script = Path.Combine(AppContext.BaseDirectory, "Media", "portal_capture.py");
        if (!File.Exists(script)) throw new FileNotFoundException("Capturador de tela não encontrado.", script);
        var start = new ProcessStartInfo("python3")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(script);
        _capture = Process.Start(start) ?? throw new InvalidOperationException("Não foi possível iniciar a captura.");
        _worker = RunAsync(_capture, _lifetime.Token);
        _heartbeat = ReportPresenceAsync(_lifetime.Token);
    }

    private async Task ReportPresenceAsync(CancellationToken cancellationToken)
    {
        StateChanged?.Invoke("pending");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                StateChanged?.Invoke(_isPublishing ? "publishing" : "pending");
        }
        catch (OperationCanceledException) { }
    }

    private async Task RunAsync(Process capture, CancellationToken cancellationToken)
    {
        Room? room = null;
        VideoSource? source = null;
        LocalVideoTrack? track = null;
        string? publicationSid = null;
        var diagnostics = capture.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            var input = capture.StandardOutput.BaseStream;
            var header = new byte[8];
            await input.ReadExactlyAsync(header, cancellationToken);
            var width = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
            var height = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
            if (width is < 320 or > 3840 || height is < 240 or > 2160)
                throw new InvalidDataException("Dimensões de captura inválidas.");

            room = new Room();
            await room.ConnectAsync(_serverUrl, _token, cancellationToken: cancellationToken);
            source = new VideoSource(width, height);
            track = LocalVideoTrack.Create("vento-screen", source);
            var publication = await room.LocalParticipant!.PublishTrackAsync(track, new LiveKit.Rtc.TrackPublishOptions
            {
                Source = TrackSource.SourceScreenshare,
                Simulcast = false,
                VideoEncoding = new VideoEncodingOptions { MaxBitrate = 1_000_000, MaxFramerate = 10 }
            }, cancellationToken);
            publicationSid = publication.Sid;

            var pixels = new byte[checked(width * height * 4)];
            while (!cancellationToken.IsCancellationRequested)
            {
                await input.ReadExactlyAsync(pixels, cancellationToken);
                var frame = new VideoFrame(width, height, VideoBufferType.Rgba, pixels);
                source.CaptureFrame(frame);
                if (!_isPublishing)
                {
                    _isPublishing = true;
                    StateChanged?.Invoke("publishing");
                }
            }
        }
        catch (Exception error) when (error is not OperationCanceledException && !_lifetime.IsCancellationRequested)
        {
            StopCaptureProcess(capture);
            var detail = await diagnostics;
            ErrorMessage = !string.IsNullOrWhiteSpace(detail) ? detail.Trim() : error.Message;
            StateChanged?.Invoke("failed");
        }
        finally
        {
            if (room is not null)
            {
                if (publicationSid is not null)
                    try { await room.LocalParticipant!.UnpublishTrackAsync(publicationSid); } catch { }
                try { await room.DisconnectAsync(); } catch { }
                room.Dispose();
            }
            track?.Dispose();
            source?.Dispose();
            StopCaptureProcess(capture);
            await capture.WaitForExitAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _lifetime.Cancel();
        if (_capture is not null) StopCaptureProcess(_capture);
        if (_worker is not null)
            try { await _worker; } catch (OperationCanceledException) { }
        if (_heartbeat is not null) await _heartbeat;
        _capture?.Dispose();
        _lifetime.Dispose();
    }

    private static void StopCaptureProcess(Process capture)
    {
        try
        {
            if (!capture.HasExited) capture.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
    }
}
