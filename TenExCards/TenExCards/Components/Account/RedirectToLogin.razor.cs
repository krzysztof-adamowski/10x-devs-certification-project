using Microsoft.AspNetCore.Components;

namespace TenExCards.Components.Account;

public partial class RedirectToLogin
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    protected override void OnInitialized()
    {
        NavigationManager.NavigateTo($"Account/Login?returnUrl={Uri.EscapeDataString(NavigationManager.Uri)}", forceLoad: true);
    }
}
