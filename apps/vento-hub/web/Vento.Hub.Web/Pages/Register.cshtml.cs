using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Vento.Hub.Web.Services;

namespace Vento.Hub.Web.Pages;

public sealed class RegisterModel(VentoApiClient identityApi) : PageModel
{
    [BindProperty]
    public RegisterInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

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

        var email = Input.Email.Trim();
        try
        {
            var result = await identityApi.RegisterAsync(Input.Username.Trim(), email, Input.Password);
            if (result != RegistrationResult.Success)
            {
                ModelState.AddModelError(string.Empty, result == RegistrationResult.InvalidAccount
                    ? "Não foi possível criar a conta. Verifique se o usuário ou e-mail já existem e se a senha tem pelo menos 10 caracteres e um número."
                    : "O cadastro não está disponível agora. Tente novamente em instantes.");
                return Page();
            }

            var account = await identityApi.LoginAsync(email, Input.Password);
            if (account is null)
                return RedirectToPage("/Login", new { registered = true, returnUrl = ReturnUrl });

            await HubSignIn.SignInAsync(HttpContext, account);
            return !string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)
                ? LocalRedirect(ReturnUrl)
                : RedirectToPage("/Account", new { created = true });
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

    public sealed class RegisterInput
    {
        [Required(ErrorMessage = "Escolha um nome de usuário.")]
        [RegularExpression("^[A-Za-z0-9][A-Za-z0-9._-]{2,31}$", ErrorMessage = "Use de 3 a 32 caracteres: letras, números, ponto, _ ou -. Comece com letra ou número.")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Informe seu e-mail.")]
        [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Crie uma senha.")]
        [MinLength(10, ErrorMessage = "A senha precisa de pelo menos 10 caracteres.")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirme a senha.")]
        [Compare(nameof(Password), ErrorMessage = "As senhas não coincidem.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
