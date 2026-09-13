using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using TenExCards.Cards;
using TenExCards.Data;

namespace TenExCards.Components.Pages;

/// <summary>
/// Find a saved card in order to edit or delete it. Deliberately not a browse surface: one capped
/// page, no paging and no sort controls — see the PRD's Non-Goals.
/// </summary>
public partial class Cards : IDisposable
{
    [Inject] private ICardStore Store { get; set; } = default!;
    [Inject] private IOptions<CardOptions> CardOptions { get; set; } = default!;

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = default!;

    private CardOptions Options => CardOptions.Value;

    private string? _ownerId;
    private string _term = string.Empty;
    private List<Card> _results = [];
    private bool _loading;
    private bool _busy;
    private string? _loadError;
    private string? _statusMessage;

    // Keyed by Id, never by list position: a delete or a re-search reorders the list under an open
    // row, and an index would then point at a different card than the learner was looking at.
    private Guid? _editingId;
    private Guid? _confirmingDeleteId;
    private string _editPrompt = string.Empty;
    private string _editAnswer = string.Empty;
    private string? _editMessage;

    private CancellationTokenSource? _searchCts;

    private bool Searching => !string.IsNullOrWhiteSpace(_term);

    private bool EditPromptOverLimit => _editPrompt.Length > CardBounds.MaxPromptCharacters;

    private bool EditAnswerOverLimit => _editAnswer.Length > CardBounds.MaxAnswerCharacters;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthenticationState;
        _ownerId = state.User.FindFirstValue(ClaimTypes.NameIdentifier);
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        // Each keystroke supersedes the one before it. Without this a slower earlier query can
        // return after a faster later one and overwrite newer results with stale ones.
        var cts = new CancellationTokenSource();
        var previous = _searchCts;
        _searchCts = cts;
        // Not disposed here: the superseded call is still unwinding and reads its own token.
        if (previous is not null)
        {
            await previous.CancelAsync();
        }

        _loading = true;
        _loadError = null;

        try
        {
            var found = await Store.FindForOwnerAsync(_ownerId!, _term, Options.MaxResults, cts.Token);

            // The token fired while this was in flight: a newer search owns the results now.
            if (cts.IsCancellationRequested)
            {
                return;
            }

            _results = [.. found];
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // Never let this reach the circuit: an unhandled exception in a handler is the generic
            // Blazor error UI, which is the never-blocks-blind guardrail failing.
            _loadError = "Your cards could not be loaded just now. Try again.";
            _results = [];
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                _loading = false;
            }
        }
    }

    private void BeginEdit(Card card)
    {
        CloseOpenRow();
        _editingId = card.Id;
        _editPrompt = card.Prompt;
        _editAnswer = card.Answer;
    }

    private void CancelEdit() => CloseOpenRow();

    private void BeginDelete(Guid id)
    {
        CloseOpenRow();
        _confirmingDeleteId = id;
    }

    private void CancelDelete() => CloseOpenRow();

    /// <summary>At most one row is ever out of view state.</summary>
    private void CloseOpenRow()
    {
        _editingId = null;
        _confirmingDeleteId = null;
        _editPrompt = string.Empty;
        _editAnswer = string.Empty;
        _editMessage = null;
        _statusMessage = null;
    }

    private async Task SaveEditAsync()
    {
        if (_busy || _editingId is not { } id)
        {
            return;
        }

        _editMessage = null;

        // Re-checked here, not only by the counter: a disabled attribute can be removed in dev
        // tools, and an over-long prompt reaches Azure SQL as a throw rather than a truncation.
        var validated = CardEdit.Validate(_editPrompt, _editAnswer);
        if (!validated.IsValid)
        {
            _editMessage = validated.Message;
            return;
        }

        _busy = true;
        try
        {
            var updated = await Store.UpdateForOwnerAsync(
                _ownerId!, id, validated.Prompt!, validated.Answer!, CancellationToken.None);

            if (!updated)
            {
                // Not yours and already gone are the same answer by design; both mean the row the
                // learner is looking at is stale.
                CloseOpenRow();
                _statusMessage = "That card no longer exists.";
                await SearchAsync();
                return;
            }

            CloseOpenRow();
            _statusMessage = "Card updated.";
            await SearchAsync();
        }
        catch (Exception)
        {
            _editMessage = "That card could not be saved just now. Nothing was lost — try again.";
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ConfirmDeleteAsync(Guid id)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            var deleted = await Store.DeleteForOwnerAsync(_ownerId!, id, CancellationToken.None);

            CloseOpenRow();
            _statusMessage = deleted ? "Card deleted." : "That card no longer exists.";
            await SearchAsync();
        }
        catch (Exception)
        {
            _statusMessage = null;
            _loadError = "That card could not be deleted just now. Try again.";
        }
        finally
        {
            _busy = false;
        }
    }

    public void Dispose()
    {
        _searchCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
