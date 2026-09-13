using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using TenExCards.Cards;
using TenExCards.Components.Account;
using TenExCards.Data;

namespace TenExCards.Components.Pages;

/// <summary>
/// Manual card entry (S-05). Statically rendered, so there is no circuit to hold and the redirect
/// on a successful save is what makes a refresh harmless.
/// </summary>
public partial class CardEntry
{
    private const int MaxWrittenDisplay = 9_999;

    [Inject] private ICardStore Store { get; set; } = default!;
    [Inject] private IdentityRedirectManager RedirectManager { get; set; } = default!;
    [Inject] private ILogger<CardEntry> Logger { get; set; } = default!;

    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm] private InputModel Input { get; set; } = default!;

    /// <summary>Arrives in the query string, so the learner can type anything. Display only —
    /// bound as a string because a non-numeric value on an int? parameter throws a 500.</summary>
    [SupplyParameterFromQuery(Name = "written")] private string? Written { get; set; }

    private string? _saveError;

    private int WrittenThisVisit =>
        int.TryParse(Written, out var written) ? Math.Clamp(written, 0, MaxWrittenDisplay) : 0;

    protected override void OnInitialized() => Input ??= new();

    private async Task SaveAsync()
    {
        _saveError = null;

        var ownerId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            // The fallback policy should make this unreachable; CardStore would throw on it.
            _saveError = "That card could not be saved just now. Nothing was lost — try again.";
            return;
        }

        try
        {
            // Trimmed to match the triage edit path, which normalises before saving.
            // edited: false — there was no candidate to edit. S-06's edit rate measures generation,
            // so a hand-written card must not count toward it.
            // CancellationToken.None, as Generate does: a save already in flight should finish, or
            // "nothing was lost" becomes a lie the learner acts on by retrying.
            await Store.SaveAsync(
                ownerId,
                Input.Prompt.Trim(),
                Input.Answer.Trim(),
                CardOrigin.Manual,
                edited: false,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Manual card save failed for {OwnerId}.", ownerId);

            // No redirect on failure: it would clear the form and lose what the learner typed.
            _saveError = "That card could not be saved just now. Nothing was lost — try again.";
            return;
        }

        // The redirect unwinds by throwing, so the save is already done and nothing runs after it.
        RedirectManager.RedirectTo("cards/new", new() { ["written"] = WrittenThisVisit + 1 });
    }

    private sealed class InputModel
    {
        [Required]
        [MaxLength(CardBounds.MaxPromptCharacters,
            ErrorMessage = "The prompt is limited to {1} characters.")]
        public string Prompt { get; set; } = "";

        [Required]
        [MaxLength(CardBounds.MaxAnswerCharacters,
            ErrorMessage = "The answer is limited to {1} characters.")]
        public string Answer { get; set; } = "";
    }
}
