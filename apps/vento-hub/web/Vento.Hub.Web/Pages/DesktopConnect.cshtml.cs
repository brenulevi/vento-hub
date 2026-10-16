using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Vento.Hub.Web.Services;

namespace Vento.Hub.Web.Pages;

[Authorize]
public sealed class DesktopConnectModel(VentoApiClient api) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string? callback, string? challenge, string? state)
    {
        if (!Uri.TryCreate(callback, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp || uri.Host != "127.0.0.1"
            || uri.AbsolutePath != "/callback" || uri.Port is < 1024 or > 65535
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || challenge is null || !Regex.IsMatch(challenge, "^[A-Za-z0-9_-]{43}$")
            || state is null || !Regex.IsMatch(state, "^[A-Za-z0-9_-]{22,128}$"))
            return BadRequest("Solicitação do aplicativo inválida.");

        var token = await HttpContext.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(token))
            return RedirectToPage("/Login", new { returnUrl = Request.Path + Request.QueryString.ToString() });

        try
        {
            var code = await api.IssueDesktopCodeAsync(token, challenge);
            return Redirect(QueryHelpers.AddQueryString(uri.ToString(), new Dictionary<string, string?>
            {
                ["code"] = code,
                ["state"] = state
            }));
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Não foi possível conectar o aplicativo. Entre novamente no hub e tente outra vez.");
        }
    }
}
