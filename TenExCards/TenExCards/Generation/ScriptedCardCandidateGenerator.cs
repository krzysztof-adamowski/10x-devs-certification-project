#if DEBUG
namespace TenExCards.Generation;

/// <summary>
/// Fixed candidates for the end-to-end journey. The Gemini free tier caps requests per model per
/// day and returns non-deterministic prose, so the browser test cannot assert against it.
/// Debug-only: see E2EHarness for what keeps this out of the deployed binary.
/// </summary>
public sealed class ScriptedCardCandidateGenerator : ICardCandidateGenerator
{
    // Three, so reject, edit-then-accept and accept-untouched are each exercised once. Prompts are
    // distinguishable because the test selects by them, and short because the test appends.
    public const string FirstPrompt = "Scripted candidate one: what is discarded first?";
    public const string SecondPrompt = "Scripted candidate two: what does the learner reword?";
    public const string ThirdPrompt = "Scripted candidate three: what is accepted untouched?";

    public Task<GenerationResult> GenerateAsync(
        string passage,
        string? focusHint,
        IProgress<GenerationProgress> progress,
        CancellationToken ct)
    {
        progress.Report(new GenerationProgress(1));

        return Task.FromResult(GenerationResult.Success(
        [
            new CandidateCard(FirstPrompt, "This one is rejected."),
            new CandidateCard(SecondPrompt, "This one is edited and then accepted."),
            new CandidateCard(ThirdPrompt, "This one is accepted as generated."),
        ]));
    }
}
#endif
