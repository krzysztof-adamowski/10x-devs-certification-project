using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Identity;
using TenExCards.Components.Account;
using TenExCards.Data;

namespace TenExCards.Components.Account.Pages;

public partial class Register
{
    [Inject]
    private UserManager<ApplicationUser> UserManager { get; set; } = default!;

    [Inject]
    private SignInManager<ApplicationUser> SignInManager { get; set; } = default!;

    [Inject]
    private ILogger<Register> Logger { get; set; } = default!;

    [Inject]
    private IdentityRedirectManager RedirectManager { get; set; } = default!;

    private IEnumerable<IdentityError>? identityErrors;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    private string? Message => identityErrors is null
        ? null
        : $"Error: {string.Join(", ", identityErrors.Select(error => error.Description))}";

    protected override void OnInitialized() => Input ??= new();

    public async Task RegisterUser(EditContext editContext)
    {
        // Setting UserName to the submitted email is what puts the unique UserNameIndex behind
        // email uniqueness — EmailIndex is non-unique and RequireUniqueEmail is only an
        // application-level check. See TenExCards/AGENTS.md and this slice's plan.
        var user = new ApplicationUser
        {
            UserName = Input.Email,
            Email = Input.Email,
        };

        var result = await UserManager.CreateAsync(user, Input.Password);

        if (!result.Succeeded)
        {
            identityErrors = result.Errors;
            return;
        }

        Logger.LogInformation("User created a new account with password.");

        await SignInManager.SignInAsync(user, isPersistent: true);
        RedirectManager.RedirectToWithStatus("/", "You have registered and are signed in.", HttpContext);
    }

    private sealed class InputModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = "";

        // The DataAnnotations length check here is UX only — the account-creating check is
        // UserManager's own configured Password.RequiredLength (16), asserted in
        // TenExCards.Tests rather than duplicated here as a second source of truth.
        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = "";

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }
}
