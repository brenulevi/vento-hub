using System;
using LiveKit.Proto;

namespace Vento.Sharing.Desktop.Media;

public readonly record struct ScreenShareOutputProfile(int Width, int Height, int MaxFps, uint EncodeMaxBitrate)
{
    public TimeSpan MinFrameInterval => TimeSpan.FromSeconds(1.0 / MaxFps);

    public string ToStatusLabel(int tierLevel, int tierCount) =>
        $"Nível {tierLevel}/{tierCount} · {Width}×{Height} · {MaxFps} fps";
}

/// <summary>Chooses capture/publish profile from LiveKit connection quality and local processing load.</summary>
public sealed class AdaptiveScreenShareController
{
    private static readonly ScreenShareOutputProfile[] Tiers =
    [
        new(1920, 1080, 30, 3_500_000),
        new(1280, 720, 24, 2_000_000),
        new(1280, 720, 15, 1_200_000),
        new(854, 480, 10, 800_000),
    ];

    private readonly object _gate = new();
    private int _tierIndex;
    private int _poorStreak;
    private int _goodStreak;
    private DateTime _lastPublishUtc = DateTime.MinValue;
    private DateTime _lastTierChangeUtc = DateTime.MinValue;

    public event Action<ScreenShareOutputProfile>? ProfileChanged;

    public ScreenShareOutputProfile Current
    {
        get { lock (_gate) return Tiers[_tierIndex]; }
    }

    public int TierLevel
    {
        get { lock (_gate) return _tierIndex + 1; }
    }

    public int TierCount => Tiers.Length;

    public static LiveKit.Rtc.TrackPublishOptions CreatePublishOptions(ScreenShareOutputProfile profile) => new()
    {
        Source = TrackSource.SourceScreenshare,
        Simulcast = false,
        DegradationPreference = DegradationPreference.MaintainFramerate,
        VideoEncoding = new LiveKit.Rtc.VideoEncodingOptions
        {
            MaxBitrate = profile.EncodeMaxBitrate,
            MaxFramerate = (uint)profile.MaxFps
        }
    };

    public void OnConnectionQuality(ConnectionQuality quality)
    {
        lock (_gate)
        {
            switch (quality)
            {
                case ConnectionQuality.QualityLost:
                case ConnectionQuality.QualityPoor:
                    _poorStreak++;
                    _goodStreak = 0;
                    if (_poorStreak >= 2)
                        StepDown();
                    break;
                case ConnectionQuality.QualityGood:
                    _poorStreak = 0;
                    _goodStreak++;
                    if (_goodStreak >= 6)
                        StepUp();
                    break;
                case ConnectionQuality.QualityExcellent:
                    _poorStreak = 0;
                    _goodStreak += 2;
                    if (_goodStreak >= 4)
                        StepUp();
                    break;
            }
        }
    }

    public void OnFrameProcessed(TimeSpan duration)
    {
        var profile = Current;
        var budget = profile.MinFrameInterval;
        if (duration <= budget + budget)
            return;

        lock (_gate)
        {
            _poorStreak++;
            _goodStreak = 0;
            if (_poorStreak >= 3)
                StepDown();
        }
    }

    public bool ShouldPublishFrame(DateTime utcNow)
    {
        lock (_gate)
        {
            var interval = Tiers[_tierIndex].MinFrameInterval;
            return utcNow - _lastPublishUtc >= interval;
        }
    }

    public void NotifyPublished(DateTime utcNow)
    {
        lock (_gate)
        {
            _lastPublishUtc = utcNow;
            _poorStreak = 0;
        }
    }

    private void StepDown()
    {
        if (_tierIndex >= Tiers.Length - 1)
            return;
        if (DateTime.UtcNow - _lastTierChangeUtc < TimeSpan.FromSeconds(8))
            return;
        _tierIndex++;
        _poorStreak = 0;
        _lastTierChangeUtc = DateTime.UtcNow;
        NotifyProfileChanged();
    }

    private void StepUp()
    {
        if (_tierIndex <= 0)
            return;
        if (DateTime.UtcNow - _lastTierChangeUtc < TimeSpan.FromSeconds(12))
            return;
        _tierIndex--;
        _goodStreak = 0;
        _lastTierChangeUtc = DateTime.UtcNow;
        NotifyProfileChanged();
    }

    public void NotifyProfileChanged() => ProfileChanged?.Invoke(Current);
}
