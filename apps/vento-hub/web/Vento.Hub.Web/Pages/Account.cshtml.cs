using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net;
using System.Security.Claims;
using Vento.Hub.Web.Services;

namespace Vento.Hub.Web.Pages;

[Authorize]
[RequestSizeLimit(2_200_000)]
public sealed class AccountModel(VentoApiClient ventoApi) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public bool Created { get; set; }
    [BindProperty(SupportsGet = true)]
    public bool ImageUpdated { get; set; }
    [BindProperty(SupportsGet = true)]
    public bool ImageRemoved { get; set; }
    [BindProperty(SupportsGet = true)]
    public bool ImageError { get; set; }
    [BindProperty]
    public IFormFile? UploadedImage { get; set; }

    public string Username { get; private set; } = string.Empty;
    public string Email => User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

    public IReadOnlyList<VentoApplication> Applications { get; private set; } = [];
    public IReadOnlyList<VentoRoomSummary> Rooms { get; private set; } = [];
    public bool CatalogUnavailable { get; private set; }
    public bool HasImage { get; private set; }

    public async Task OnGetAsync()
    {
        Username = User.Identity?.Name ?? string.Empty;
        try
        {
            Applications = await ventoApi.GetApplicationsAsync();
        }
        catch (HttpRequestException)
        {
            CatalogUnavailable = true;
        }
        catch (TaskCanceledException)
        {
            CatalogUnavailable = true;
        }
        var token = await HttpContext.GetTokenAsync("access_token");
        if (!string.IsNullOrEmpty(token))
        {
            try { Username = (await ventoApi.GetProfileAsync(token))?.Username ?? Username; }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            try { HasImage = await ventoApi.HasAccountImageAsync(token); }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            try { Rooms = await ventoApi.GetRoomsAsync(token); }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
        }
    }

    public async Task<IActionResult> OnGetImageAsync()
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (string.IsNullOrEmpty(token)) return Unauthorized();
        try
        {
            var image = await ventoApi.GetAccountImageAsync(token);
            return image is null ? NotFound() : File(image.Value.Data, image.Value.ContentType);
        }
        catch (HttpRequestException) { return StatusCode(502); }
        catch (TaskCanceledException) { return StatusCode(504); }
    }

    public async Task<IActionResult> OnPostImageAsync()
    {
        var file = UploadedImage;
        if (file is null || file.Length is < 1 or > 2 * 1024 * 1024
            || file.ContentType is not ("image/png" or "image/jpeg"))
            return RedirectToPage("/Account", new { imageError = true });

        var token = await HttpContext.GetTokenAsync("access_token");
        if (string.IsNullOrEmpty(token)) return Challenge();
        try
        {
            await using var stream = file.OpenReadStream();
            var status = await ventoApi.UploadAccountImageAsync(token, stream, file.ContentType);
            return RedirectToPage("/Account", status == HttpStatusCode.NoContent
                ? new { imageUpdated = true } : new { imageError = true });
        }
        catch (HttpRequestException) { return RedirectToPage("/Account", new { imageError = true }); }
        catch (TaskCanceledException) { return RedirectToPage("/Account", new { imageError = true }); }
    }

    public async Task<IActionResult> OnPostRemoveImageAsync()
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (string.IsNullOrEmpty(token)) return Challenge();
        try
        {
            await ventoApi.RemoveAccountImageAsync(token);
            return RedirectToPage("/Account", new { imageRemoved = true });
        }
        catch (HttpRequestException) { return RedirectToPage("/Account", new { imageError = true }); }
        catch (TaskCanceledException) { return RedirectToPage("/Account", new { imageError = true }); }
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Index");
    }
}
