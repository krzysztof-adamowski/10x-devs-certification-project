using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using TenExCards.Cards;
using TenExCards.Data;
using TenExCards.Generation;

namespace TenExCards.Components.Pages;

/// <summary>
/// The whole learner loop in one component, because the candidate batch lives in this component's
/// state and nowhere else — navigating between routes would dispose it.
/// </summary>
public partial class Generate : IAsyncDisposable
{
    private enum Stage
    {
        Composing,
        Generating,
        Triaging,
        Summary,
        Failed,
    }

    [Inject] private ICardCandidateGenerator Generator { get; set; } = default!;
    [Inject] private ICardStore Store { get; set; } = default!;
    [Inject] private IOptions<GenerationOptions> GenerationOptions { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    [CascadingParameter] private Task<AuthenticationState> AuthenticationState { get; set; } = default!;

    private GenerationOptions Options => GenerationOptions.Value;

    private Stage _stage = Stage.Composing;
    private string _passage = string.Empty;
    private string _focusHint = string.Empty;
    private string? _validationMessage;
    private string? _failureMessage;
    private string? _ownerId;

    private readonly List<CandidateCard> _pending = [];
    private int _batchSize;
    private int _triagedCount;
    private int _saved;
    private int _discarded;
    private string? _saveError;

    private CancellationTokenSource? _cts;
    private PeriodicTimer? _elapsedTimer;
    private DateTimeOffset _startedAt;
    private int _elapsedSeconds;
    private int _chunks;
    private bool _cancelledByLearner;

    private IJSObjectReference? _unloadModule;
    private bool _unloadWarningRegistered;

    private CandidateCard Current => _pending[0];

    private bool PassageOverLimit => _passage.Length > Options.MaxPassageCharacters;

    private bool FocusHintOverLimit => _focusHint.Length > Options.MaxFocusHintCharacters;

    private bool CanSubmit => !string.IsNullOrWhiteSpace(_passage) && !PassageOverLimit && !FocusHintOverLimit;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthenticationState;
        _ownerId = state.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private async Task SubmitAsync()
    {
        _validationMessage = null;
        _failureMessage = null;

        // Re-checked here, not only on the submit button: a disabled attribute is a courtesy and
        // can be removed in dev tools.
        if (!PassageBounds.IsWithinLimit(_passage, Options.MaxPassageCharacters))
        {
            _validationMessage = string.IsNullOrWhiteSpace(_passage)
                ? "Paste a passage first."
                : $"That passage is {_passage.Length:N0} characters. The limit is {Options.MaxPassageCharacters:N0}.";
            _stage = Stage.Composing;
            return;
        }

        if (FocusHintOverLimit)
        {
            _validationMessage = $"The focus hint is limited to {Options.MaxFocusHintCharacters} characters.";
            _stage = Stage.Composing;
            return;
        }

        _stage = Stage.Generating;
        _chunks = 0;
        _elapsedSeconds = 0;
        _cancelledByLearner = false;
        _startedAt = DateTimeOffset.UtcNow;
        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(Options.TimeoutSeconds));

        // Rendered before the await, or the 2s acknowledgement measures the model instead of the form.
        StateHasChanged();
        StartElapsedTimer();

        var progress = new Progress<GenerationProgress>(p =>
        {
            _chunks = p.ChunksReceived;
            _ = InvokeAsync(StateHasChanged);
        });

        GenerationResult result;
        try
        {
            result = await Generator.GenerateAsync(
                _passage,
                string.IsNullOrWhiteSpace(_focusHint) ? null : _focusHint,
                progress,
                _cts.Token);
        }
        finally
        {
            StopElapsedTimer();
        }

        if (_cancelledByLearner)
        {
            _stage = Stage.Composing;
            return;
        }

        if (!result.IsSuccess)
        {
            _failureMessage = result.Message;
            _stage = Stage.Failed;
            return;
        }

        _pending.Clear();
        _pending.AddRange(result.Candidates!);
        _batchSize = _pending.Count;
        _triagedCount = 0;
        _saved = 0;
        _discarded = 0;
        _saveError = null;

        // The passage goes before the triage state is entered. There is no moment in which both
        // candidates and the text they came from exist.
        _passage = string.Empty;
        _focusHint = string.Empty;

        _stage = Stage.Triaging;
        await SetUnloadWarningAsync(true);
    }

    private async Task CancelAsync()
    {
        _cancelledByLearner = true;
        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }
    }

    private async Task AcceptAsync()
    {
        _saveError = null;

        try
        {
            await Store.SaveAsync(_ownerId!, Current.Prompt, Current.Answer, CardOrigin.Generated, CancellationToken.None);
        }
        catch (Exception)
        {
            // Never advance and never move to Failed: the card was not saved, and Failed would
            // re-render a passage that is gone by design. The candidate stays put so a retry
            // re-issues the same save rather than queueing a second one.
            _saveError = "That card could not be saved just now. Nothing was lost — try again.";
            return;
        }

        _saved++;
        await AdvanceAsync();
    }

    private async Task RejectAsync()
    {
        _saveError = null;
        _discarded++;
        await AdvanceAsync();
    }

    private async Task AdvanceAsync()
    {
        _pending.RemoveAt(0);
        _triagedCount++;

        if (_pending.Count == 0)
        {
            // The stage moves BEFORE the interop await. Blazor renders at an event handler's first
            // yielding await, and on the reject path that await is the JS call — so leaving the
            // stage on Triaging here renders an empty list through Current and throws.
            _stage = Stage.Summary;
            await SetUnloadWarningAsync(false);
        }
    }

    private void StartOver()
    {
        _stage = Stage.Composing;
        _validationMessage = null;
        _failureMessage = null;
        _saveError = null;
        _passage = string.Empty;
        _focusHint = string.Empty;
    }

    private void StartElapsedTimer()
    {
        _elapsedTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        _ = TickAsync(_elapsedTimer);
    }

    private async Task TickAsync(PeriodicTimer timer)
    {
        try
        {
            while (await timer.WaitForNextTickAsync())
            {
                _elapsedSeconds = (int)(DateTimeOffset.UtcNow - _startedAt).TotalSeconds;
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void StopElapsedTimer()
    {
        _elapsedTimer?.Dispose();
        _elapsedTimer = null;
    }

    private async Task SetUnloadWarningAsync(bool active)
    {
        if (active == _unloadWarningRegistered)
        {
            return;
        }

        try
        {
            // Assets is a protected ComponentBase property the framework fills in, NOT a DI
            // service — injecting ResourceAssetCollection compiles and then throws at first render.
            _unloadModule ??= await JS.InvokeAsync<IJSObjectReference>(
                "import", "./" + Assets["Components/Pages/Generate.razor.js"]);

            await _unloadModule.InvokeVoidAsync(active ? "register" : "unregister");
            _unloadWarningRegistered = active;
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        StopElapsedTimer();
        _cts?.Dispose();

        if (_unloadModule is not null)
        {
            try
            {
                await _unloadModule.InvokeVoidAsync("unregister");
                await _unloadModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The browser is already gone; there is nothing to unregister and nothing to report.
            }
            catch (JSException)
            {
            }
        }

        GC.SuppressFinalize(this);
    }
}
