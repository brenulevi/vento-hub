using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Vento.Sharing.Desktop.Media;

public sealed class DesktopPublisherSession : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _path;
    private readonly string _html;
    private readonly byte[] _script;
    private readonly Task _listenerTask;
    private readonly Task _watchdogTask;
    private DateTimeOffset _lastReport = DateTimeOffset.UtcNow;
    private bool _stopRequested;
    private int _closed;

    public event Action<string>? StateChanged;
    public string Address { get; }

    public DesktopPublisherSession(string serverUrl, string mediaToken)
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        _path = $"/{secret}/";
        var template = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Media", "publisher.html"));
        _html = template.Replace("__CONNECTION__", JsonSerializer.Serialize(new { Url = serverUrl, Token = mediaToken }));
        _script = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Media", "publisher.js"));

        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var origin = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(origin);
        _listener.Start();
        Address = origin.TrimEnd('/') + _path;
        _listenerTask = ListenAsync();
        _watchdogTask = WatchAsync();
    }

    public void OpenBrowser()
    {
        _ = Process.Start(new ProcessStartInfo(Address) { UseShellExecute = true })
            ?? throw new InvalidOperationException("Nenhum navegador foi encontrado.");
    }

    public async Task StopAsync(bool waitForBrowser = true)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _stopRequested = true;
        if (waitForBrowser)
            await Task.Delay(2500);
        _lifetime.Cancel();
        _listener.Stop();
        _listener.Close();
        try { await _listenerTask; } catch (HttpListenerException) { }
        try { await _watchdogTask; } catch (OperationCanceledException) { }
    }

    private async Task ListenAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception error) when (error is HttpListenerException or ObjectDisposedException) { break; }
            _ = HandleAsync(context);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        try
        {
            if (!request.Url!.AbsolutePath.StartsWith(_path, StringComparison.Ordinal))
            {
                response.StatusCode = 404;
                return;
            }
            var suffix = request.Url.AbsolutePath[_path.Length..];
            if (request.HttpMethod == "GET" && suffix.Length == 0)
            {
                await WriteAsync(response, Encoding.UTF8.GetBytes(_html), "text/html; charset=utf-8");
                return;
            }
            if (request.HttpMethod == "GET" && suffix == "publisher.js")
            {
                await WriteAsync(response, _script, "text/javascript; charset=utf-8");
                return;
            }
            if (request.HttpMethod == "GET" && suffix == "control")
            {
                await WriteAsync(response, JsonSerializer.SerializeToUtf8Bytes(new { stop = _stopRequested }),
                    "application/json");
                return;
            }
            if (request.HttpMethod == "POST" && suffix == "status"
                && request.HasEntityBody && request.ContentLength64 < 1024
                && (string.IsNullOrEmpty(request.Headers["Origin"])
                    || request.Headers["Origin"] == request.Url.GetLeftPart(UriPartial.Authority)))
            {
                using var document = await JsonDocument.ParseAsync(request.InputStream);
                var state = document.RootElement.TryGetProperty("state", out var value) ? value.GetString() : null;
                if (state is "pending" or "publishing" or "stopped" or "error")
                {
                    _lastReport = DateTimeOffset.UtcNow;
                    StateChanged?.Invoke(state);
                    response.StatusCode = 204;
                    return;
                }
            }
            response.StatusCode = 400;
        }
        catch (Exception error) when (error is IOException or JsonException or ObjectDisposedException)
        {
            response.StatusCode = 400;
        }
        finally
        {
            try { response.Close(); } catch (ObjectDisposedException) { }
        }
    }

    private async Task WatchAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(_lifetime.Token))
        {
            if (DateTimeOffset.UtcNow - _lastReport > TimeSpan.FromSeconds(40))
            {
                StateChanged?.Invoke("lost");
                return;
            }
        }
    }

    private static async Task WriteAsync(HttpListenerResponse response, byte[] payload, string contentType)
    {
        response.ContentType = contentType;
        response.ContentLength64 = payload.Length;
        await response.OutputStream.WriteAsync(payload);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifetime.Dispose();
    }
}
