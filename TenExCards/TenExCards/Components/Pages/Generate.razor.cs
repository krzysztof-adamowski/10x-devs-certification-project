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
    [Inject] private ITriageRecorder Recorder { get; set; } = default!;
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

    private TriageSession? _session;
    // null when the open failed; the record calls then no-op. Triage never depends on it.
    private Guid? _batchId;
    private string? _saveError;

    private bool _editing;
    private string _editPrompt = string.Empty;
    private string _editAnswer = string.Empty;
    private string? _editValidationMessage;

    private CancellationTokenSource? _cts;
    private PeriodicTimer? _elapsedTimer;
    private DateTimeOffset _startedAt;
    private int _elapsedSeconds;
    private int _chunks;
    private bool _cancelledByLearner;

    private IJSObjectReference? _unloadModule;
    private bool _unloadWarningRegistered;

    // Blazor does not serialise event handlers, and every triage handler mutates _pending across an
    // await. Without this a second click can save the same card twice and advance past the next one
    // unseen. No lock: all mutation runs on the circuit dispatcher.
    private bool _busy;

    private CandidateCard Current => _session!.Current;

    private bool PassageOverLimit => _passage.Length > Options.MaxPassageCharacters;

    private bool FocusHintOverLimit => _focusHint.Length > Options.MaxFocusHintCharacters;

    private bool CanSubmit => !string.IsNullOrWhiteSpace(_passage) && !PassageOverLimit && !FocusHintOverLimit;

    private bool CanCommitEdit => CandidateEdit.IsCommittable(_editPrompt, _editAnswer);

    private bool EditPromptOverLimit => _editPrompt.Length > CardBounds.MaxPromptCharacters;

    private bool EditAnswerOverLimit => _editAnswer.Length > CardBounds.MaxAnswerCharacters;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthenticationState;
        _ownerId = state.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private async Task SubmitAsync()
    {
        // A second click before the render reaches the browser would start a second generation and
        // spend the daily quota twice.
        if (_busy)
        {
            return;
        }

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

        _busy = true;
        _stage = Stage.Generating;
        _chunks = 0;
        _elapsedSeconds = 0;
        _cancelledByLearner = false;
        _startedAt = DateTimeOffset.UtcNow;

        _cts?.Dispose();
        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(Options.TimeoutSeconds));

        // Rendered before the await, or the 2s acknowledgement measures the model instead of the form.
        StateHasChanged();
        StartElapsedTimer();

        var progress = new Progress<GenerationProgress>(p =>
        {
            _chunks = p.ChunksReceived;
            _ = InvokeAsync(StateHasChanged);
        });

        try
        {
            GenerationResult result;
            try
            {
                result = await Generator.GenerateAsync(
                    _passage,
                    string.IsNullOrWhiteSpace(_focusHint) ? null : _focusHint,
                    progress,
                    _cts.Token);
            }
            catch (Exception)
            {
                if (_cancelledByLearner)
                {
                    _stage = Stage.Composing;
                    return;
                }

                _failureMessage = "Something went wrong while generating. Your passage is still here — please try again.";
                _stage = Stage.Failed;
                return;
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

            _session = new TriageSession(result.Candidates!);
            _saveError = null;
            ClearEdit();

            // The passage goes before the triage state is entered. There is no moment in which both
            // candidates and the text they came from exist.
            _passage = string.Empty;
            _focusHint = string.Empty;

            // Below the clearing, never above: an await here would render with the passage and its
            // candidates both alive in the circuit.
            _batchId = await Recorder.OpenBatchAsync(
                _ownerId!, _session.BatchSize, CancellationToken.None);

            _stage = Stage.Triaging;
            await SetUnloadWarningAsync(true);
        }
        finally
        {
            _busy = false;
        }
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
        if (!TryBeginTriage())
        {
            return;
        }

        try
        {
            _saveError = null;
            _editValidationMessage = null;

            var prompt = _editing ? _editPrompt.Trim() : Current.Prompt;
            var answer = _editing ? _editAnswer.Trim() : Current.Answer;

            // Re-checked here, not only on the button: a disabled attribute can be removed in dev
            // tools, and an over-long prompt reaches Azure SQL as a throw rather than a truncation.
            if (_editing && !CandidateEdit.IsCommittable(prompt, answer))
            {
                _editValidationMessage = CardEdit.Validate(prompt, answer).Message;
                return;
            }

            var edited = _editing && CandidateEdit.WasEdited(Current, prompt, answer);

            try
            {
                await Store.SaveAsync(
                    _ownerId!,
                    prompt,
                    answer,
                    CardOrigin.Generated,
                    edited,
                    CancellationToken.None);
            }
            catch (Exception)
            {
                // Never advance and never move to Failed: the card was not saved, and Failed would
                // re-render a passage that is gone by design. Edit mode and the buffer stay put too,
                // so a retry re-issues the same save rather than queueing a second one.
                _saveError = "That card could not be saved just now. Nothing was lost — try again.";
                return;
            }

            _session!.Accept(edited);
            await Recorder.RecordAcceptAsync(_ownerId!, _batchId, edited, CancellationToken.None);
            await AdvanceAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RejectAsync()
    {
        if (!TryBeginTriage())
        {
            return;
        }

        try
        {
            _saveError = null;
            _session!.Reject();
            await Recorder.RecordRejectAsync(_ownerId!, _batchId, CancellationToken.None);
            await AdvanceAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    private void BeginEdit()
    {
        _editPrompt = Current.Prompt;
        _editAnswer = Current.Answer;
        _editValidationMessage = null;
        _editing = true;
    }

    /// <summary>The candidate was never touched, so leaving edit mode restores it by itself.</summary>
    private void CancelEdit() => ClearEdit();

    private void ClearEdit()
    {
        _editing = false;
        _editPrompt = string.Empty;
        _editAnswer = string.Empty;
        _editValidationMessage = null;
    }

    /// <summary>
    /// Claims the triage lock, or refuses when one is already in flight or the batch has emptied.
    /// The count check is what stops a late handler indexing past the end of the list.
    /// </summary>
    private bool TryBeginTriage()
    {
        if (_busy || _stage is not Stage.Triaging || _session is null || _session.IsComplete)
        {
            return false;
        }

        _busy = true;
        return true;
    }

    private async Task AdvanceAsync()
    {
        // Cleared BEFORE the interop await, alongside the stage. Blazor renders at an event
        // handler's first yielding await, so an edit mode left pointing at a departed candidate
        // renders through Current and throws.
        ClearEdit();

        if (_session!.IsComplete)
        {
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
        ClearEdit();
    }

    private void StartElapsedTimer()
    {
        // Never orphan a previous timer: its tick loop would keep rendering against a live component.
        StopElapsedTimer();
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
