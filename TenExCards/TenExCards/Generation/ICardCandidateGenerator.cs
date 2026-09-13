namespace TenExCards.Generation;

/// <summary>
/// The one seam the tests stub. The timeout belongs to the caller: implementations observe
/// <paramref name="ct"/> and return <see cref="GenerationFailure.Timeout"/> rather than throwing.
/// </summary>
public interface ICardCandidateGenerator
{
    /// <param name="passage">Never logged, by any implementation. Nor is <paramref name="focusHint"/>.</param>
    Task<GenerationResult> GenerateAsync(
        string passage,
        string? focusHint,
        IProgress<GenerationProgress> progress,
        CancellationToken ct);
}
