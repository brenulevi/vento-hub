using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Proto;
using LiveKit.Rtc;

namespace Vento.Sharing.Desktop.Media;

/// <summary>LiveKit room connection with adaptive screen-share publication.</summary>
internal sealed class PublisherScreenShareSession : IAsyncDisposable
{
    private readonly AdaptiveScreenShareController _adaptive = new();
    private Room? _room;
    private VideoSource? _source;
    private LocalVideoTrack? _track;
    private string? _publicationSid;
    private int _publishedWidth;
    private int _publishedHeight;
    private byte[]? _scaledBuffer;

    public event Action<string>? OutputProfileStatus;

    public PublisherScreenShareSession()
    {
        _adaptive.ProfileChanged += profile =>
            OutputProfileStatus?.Invoke(profile.ToStatusLabel(_adaptive.TierLevel, _adaptive.TierCount));
    }

    public async Task ConnectAsync(string serverUrl, string token, CancellationToken cancellationToken)
    {
        var room = new Room();
        room.ConnectionQualityChanged += OnConnectionQualityChanged;
        _room = room;
        await room.ConnectAsync(
            serverUrl,
            token,
            new LiveKit.Rtc.RoomOptions { AutoSubscribe = true, Dynacast = true },
            cancellationToken);
        await EnsureVideoPipelineAsync(cancellationToken);
        _adaptive.NotifyProfileChanged();
    }

    private void OnConnectionQualityChanged(object? sender, ConnectionQualityChangedEventArgs args)
    {
        if (_room?.LocalParticipant is null)
            return;
        if (!string.Equals(args.Participant.Identity, _room.LocalParticipant.Identity, StringComparison.Ordinal))
            return;
        _adaptive.OnConnectionQuality(args.Quality);
    }

    public async Task<bool> TryPublishRgbaAsync(
        byte[] pixels,
        int sourceWidth,
        int sourceHeight,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        if (!_adaptive.ShouldPublishFrame(utcNow))
            return false;

        var profile = _adaptive.Current;
        await EnsureVideoPipelineAsync(cancellationToken);

        var processing = Stopwatch.StartNew();
        byte[] payload;
        if (sourceWidth == profile.Width && sourceHeight == profile.Height)
            payload = pixels;
        else
        {
            _scaledBuffer ??= new byte[checked(profile.Width * profile.Height * 4)];
            ScreenShareFrameScaler.ScaleRgba(pixels, sourceWidth, sourceHeight, _scaledBuffer, profile.Width, profile.Height);
            payload = _scaledBuffer;
        }
        processing.Stop();
        _adaptive.OnFrameProcessed(processing.Elapsed);

        _source!.CaptureFrame(new VideoFrame(profile.Width, profile.Height, VideoBufferType.Rgba, payload));
        _adaptive.NotifyPublished(utcNow);
        return true;
    }

    public async Task<bool> TryPublishBgraAsync(
        IntPtr sourcePointer,
        int sourceWidth,
        int sourceHeight,
        int sourceStride,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        if (!_adaptive.ShouldPublishFrame(utcNow))
            return false;

        var profile = _adaptive.Current;
        await EnsureVideoPipelineAsync(cancellationToken);

        var processing = Stopwatch.StartNew();
        _scaledBuffer ??= new byte[checked(profile.Width * profile.Height * 4)];
        ScreenShareFrameScaler.ScaleBgra(
            sourcePointer,
            sourceWidth,
            sourceHeight,
            sourceStride,
            _scaledBuffer,
            profile.Width,
            profile.Height);
        processing.Stop();
        _adaptive.OnFrameProcessed(processing.Elapsed);

        _source!.CaptureFrame(new VideoFrame(profile.Width, profile.Height, VideoBufferType.Bgra, _scaledBuffer));
        _adaptive.NotifyPublished(utcNow);
        return true;
    }

    private async Task EnsureVideoPipelineAsync(CancellationToken cancellationToken)
    {
        var profile = _adaptive.Current;
        if (_source is not null && _publishedWidth == profile.Width && _publishedHeight == profile.Height)
            return;

        if (_room?.LocalParticipant is null)
            throw new InvalidOperationException("A sala não está conectada.");

        if (_publicationSid is not null)
        {
            try { await _room.LocalParticipant.UnpublishTrackAsync(_publicationSid); } catch { }
            _publicationSid = null;
        }

        _track?.Dispose();
        _source?.Dispose();
        _scaledBuffer = null;

        _source = new VideoSource(profile.Width, profile.Height);
        _track = LocalVideoTrack.Create("vento-screen", _source);
        var publication = await _room.LocalParticipant.PublishTrackAsync(
            _track,
            AdaptiveScreenShareController.CreatePublishOptions(profile),
            cancellationToken);
        _publicationSid = publication.Sid;
        _publishedWidth = profile.Width;
        _publishedHeight = profile.Height;
        _adaptive.NotifyProfileChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (_room is not null)
        {
            if (_publicationSid is not null)
            {
                try { await _room.LocalParticipant!.UnpublishTrackAsync(_publicationSid); } catch { }
            }
            try { await _room.DisconnectAsync(); } catch { }
            _room.Dispose();
            _room = null;
        }
        _track?.Dispose();
        _source?.Dispose();
    }
}
