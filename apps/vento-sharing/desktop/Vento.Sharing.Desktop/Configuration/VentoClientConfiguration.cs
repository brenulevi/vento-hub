using System;
using System.IO;
using System.Text.Json;

namespace Vento.Sharing.Desktop.Configuration;

/// <summary>Endpoints do produto Vento (API + Hub), como no cliente estável do Discord.</summary>
public sealed class VentoClientConfiguration
{
    private static readonly Lazy<VentoClientConfiguration> Instance = new(Load);

    public static VentoClientConfiguration Current => Instance.Value;

    public required string ApiBaseUrl { get; init; }
    public required string HubBaseUrl { get; init; }
    public bool ShowAdvancedServerSettings { get; init; }

    private static VentoClientConfiguration Load()
    {
        var model = new VentoSection
        {
            ApiBaseUrl = "http://127.0.0.1:5080",
            HubBaseUrl = "http://127.0.0.1:5081",
            ShowAdvancedServerSettings = false
        };
        MergeFile(model, Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
#if DEBUG
        MergeFile(model, Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json"));
#endif
        ApplyEnvironment(model);
        return new VentoClientConfiguration
        {
            ApiBaseUrl = model.ApiBaseUrl.TrimEnd('/'),
            HubBaseUrl = model.HubBaseUrl.TrimEnd('/'),
            ShowAdvancedServerSettings = model.ShowAdvancedServerSettings
        };
    }

    private static void MergeFile(VentoSection target, string path)
    {
        if (!File.Exists(path))
            return;
        try
        {
            using var stream = File.OpenRead(path);
            var file = JsonSerializer.Deserialize<AppSettingsFile>(stream);
            if (file?.Vento is null)
                return;
            if (!string.IsNullOrWhiteSpace(file.Vento.ApiBaseUrl))
                target.ApiBaseUrl = file.Vento.ApiBaseUrl;
            if (!string.IsNullOrWhiteSpace(file.Vento.HubBaseUrl))
                target.HubBaseUrl = file.Vento.HubBaseUrl;
            target.ShowAdvancedServerSettings = file.Vento.ShowAdvancedServerSettings;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
        }
    }

    private static void ApplyEnvironment(VentoSection target)
    {
        var api = Environment.GetEnvironmentVariable("VENTO_API_BASE_URL");
        if (!string.IsNullOrWhiteSpace(api))
            target.ApiBaseUrl = api.TrimEnd('/');

        var hub = Environment.GetEnvironmentVariable("VENTO_HUB_BASE_URL");
        if (!string.IsNullOrWhiteSpace(hub))
            target.HubBaseUrl = hub.TrimEnd('/');
    }

    private sealed class AppSettingsFile
    {
        public VentoSection? Vento { get; set; }
    }

    private sealed class VentoSection
    {
        public string ApiBaseUrl { get; set; } = string.Empty;
        public string HubBaseUrl { get; set; } = string.Empty;
        public bool ShowAdvancedServerSettings { get; set; }
    }
}
