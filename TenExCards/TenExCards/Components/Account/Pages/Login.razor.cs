using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using TenExCards.Components.Account;
using TenExCards.Data;

namespace TenExCards.Components.Account.Pages;

public partial class Login
{
    [Inject]
    private SignInManager<ApplicationUser> SignInManager { get; set; } = default!;

    [Inject]
    private ILogger<Login> Logger { get; set; } = default!;

    [Inject]
    private IdentityRedirectManager RedirectManager { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    private string? errorMessage;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    protected override void OnInitialized() => Input ??= new();

    public async Task LoginUser()
    {
        // lockoutOnFailure: true — the PRD requires lockout on repeated failed sign-ins, not just
        // failed attempts counted forever. Always persistent: this app has no "remember me"
        // control, and the desired seven-day sliding session (ConfigureApplicationCookie in
        // Program.cs) is meaningless for a cookie the browser drops on close.
        var result = await SignInManager.PasswordSignInAsync(
            Input.Email, Input.Password, isPersistent: true, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            Logger.LogInformation("User logged in.");
            RedirectManager.RedirectTo(ReturnUrl);
        }
        else if (result.IsLockedOut)
        {
            Logger.LogWarning("User account locked out.");
            errorMessage = "Error: This account is locked out due to repeated failed sign-in attempts. Try again in a few minutes.";
        }
        else
        {
            errorMessage = "Error: Invalid login attempt.";
        }
    }

    private sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";
    }
}
