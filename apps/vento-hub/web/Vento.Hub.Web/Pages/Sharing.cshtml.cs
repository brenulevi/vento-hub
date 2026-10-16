using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Vento.Hub.Web.Services;

namespace Vento.Hub.Web.Pages;

[Authorize]
public sealed class SharingModel(VentoApiClient ventoApi) : PageModel
{
    public string ConnectionJson { get; private set; } = "{}";
    public string RoomName { get; private set; } = string.Empty;
    public Guid RoomId { get; private set; }
    public string? ViewerError { get; private set; }
    public bool HasConnection => ViewerError is null;

    public async Task<IActionResult> OnGetAsync(Guid roomId, string? invite)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (string.IsNullOrEmpty(token)) return Challenge();
        MediaConnection? connection;
        try { connection = await ventoApi.GetMediaConnectionAsync(token, roomId); }
        catch (HttpRequestException) { return StatusCode(502); }
        catch (TaskCanceledException) { return StatusCode(504); }
        if (connection is null && !string.IsNullOrWhiteSpace(invite))
        {
            try
            {
                if (await ventoApi.JoinRoomAsync(token, roomId, invite.Trim().ToUpperInvariant()))
                    connection = await ventoApi.GetMediaConnectionAsync(token, roomId);
            }
            catch (HttpRequestException) { return StatusCode(502); }
            catch (TaskCanceledException) { return StatusCode(504); }
        }

        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (connection is null)
        {
            ViewerError = "Não foi possível entrar nesta sala. Confira o convite ou peça ao anfitrião um novo link.";
            RoomName = "Sala Vento";
            return Page();
        }
        ConnectionJson = JsonSerializer.Serialize(connection);
        RoomName = connection.RoomName;
        RoomId = roomId;
        return Page();
    }
}
