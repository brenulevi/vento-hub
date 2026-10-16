using Microsoft.AspNetCore.Mvc.RazorPages;
using Vento.Hub.Web.Services;

namespace Vento.Hub.Web.Pages;

public sealed class IndexModel(VentoApiClient ventoApi) : PageModel
{
    public IReadOnlyList<VentoApplication> Applications { get; private set; } = [];
    public bool CatalogUnavailable { get; private set; }

    public async Task OnGetAsync()
    {
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
    }
}
