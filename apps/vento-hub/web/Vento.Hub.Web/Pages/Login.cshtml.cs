using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Vento.Hub.Web.Services;

namespace Vento.Hub.Web.Pages;

public sealed class LoginModel(VentoApiClient identityApi) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Registered { get; set; }

    public IActionResult OnGet()
    {
        return User.Identity?.IsAuthenticated == true
            ? !string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? LocalRedirect(ReturnUrl) : RedirectToPage("/Account")
            : Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var identifier = Input.Identifier.Trim();
        try
        {
            var account = await identityApi.LoginAsync(identifier, Input.Password);
            if (account is null)
            {
                ModelState.AddModelError(string.Empty, "Usuário, e-mail ou senha incorretos.");
                return Page();
            }

            await HubSignIn.SignInAsync(HttpContext, account);
            return !string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)
                ? LocalRedirect(ReturnUrl)
                : RedirectToPage("/Account");
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError(string.Empty, "Não foi possível conectar ao serviço de contas. Tente novamente em instantes.");
            return Page();
        }
        catch (TaskCanceledException)
        {
            ModelState.AddModelError(string.Empty, "O serviço de contas demorou para responder. Tente novamente.");
            return Page();
        }
    }

    public sealed class LoginInput
    {
        [Required(ErrorMessage = "Informe seu usuário ou e-mail.")]
        public string Identifier { get; set; } = string.Empty;

        [Required(ErrorMessage = "Informe sua senha.")]
        public string Password { get; set; } = string.Empty;
    }
}
