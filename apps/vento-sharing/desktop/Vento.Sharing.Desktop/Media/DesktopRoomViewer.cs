using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Proto;
using LiveKit.Rtc;

namespace Vento.Sharing.Desktop.Media;

/// <summary>Receives the room's screen track for the desktop stage.</summary>
public sealed class DesktopRoomViewer : IAsyncDisposable
{
    private readonly string _serverUrl;
    private readonly string _token;
    private readonly CancellationTokenSource _lifetime = new();
    private Room? _room;
    private volatile string? _activeTrackSid;
    private volatile string? _activePublicationSid;
    private int _closed;

    public event Action<int, int, byte[]?>? FrameReceived;

    public DesktopRoomViewer(string serverUrl, string token)
    {
        _serverUrl = serverUrl;
        _token = token;
    }

    public async Task StartAsync()
    {
        var room = new Room();
        _room = room;
        room.TrackSubscribed += (_, args) =>
        {
            if (args.Track is RemoteVideoTrack && args.Publication.Source == TrackSource.SourceScreenshare)
            {
                _activeTrackSid = args.Track.Sid;
                _activePublicationSid = args.Publication.Sid;
                _ = ReceiveAsync(args.Track, _lifetime.Token);
            }
        };
        room.TrackUnsubscribed += (_, args) =>
        {
            if (args.Track.Sid == _activeTrackSid)
                ClearActiveTrack();
        };
        room.TrackUnpublished += (_, args) =>
        {
            if (args.Publication.Source != TrackSource.SourceScreenshare) return;
            if (args.Publication.Sid == _activePublicationSid)
                ClearActiveTrack();
        };
        await room.ConnectAsync(_serverUrl, _token,
            new LiveKit.Rtc.RoomOptions { AutoSubscribe = true }, _lifetime.Token);
    }

    private void ClearActiveTrack()
    {
        _activeTrackSid = null;
        _activePublicationSid = null;
        FrameReceived?.Invoke(0, 0, null);
    }

    private async Task ReceiveAsync(Track track, CancellationToken cancellationToken)
    {
        var trackSid = track.Sid;
        try
        {
            using var stream = new VideoStream(track, VideoBufferType.Bgra, capacity: 1);
            await foreach (var item in stream.WithCancellation(cancellationToken))
            {
                if (trackSid != _activeTrackSid) break;
                var frame = item.Frame;
                var pixels = frame.DataBytes.ToArray();
                FrameReceived?.Invoke(frame.Width, frame.Height, pixels);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
        finally
        {
            if (trackSid == _activeTrackSid)
                ClearActiveTrack();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _lifetime.Cancel();
        if (_room is not null)
        {
            try { await _room.DisconnectAsync(); } catch { }
            _room.Dispose();
        }
        _lifetime.Dispose();
        FrameReceived?.Invoke(0, 0, null);
    }
}
