namespace TenExCards.Generation;

public enum GenerationFailure
{
    /// <summary>The caller's token fired — the ceiling elapsed, or the learner cancelled.</summary>
    Timeout = 1,

    ProviderError = 2,

    /// <summary>Answered, but not matching the requested schema.</summary>
    Malformed = 3,

    /// <summary>Fewer than <see cref="GenerationOptions.MinCandidates"/> survived the rules.</summary>
    Refused = 4,

    /// <summary>Every configured model returned 429 — the free tier's daily quota is spent.</summary>
    QuotaExhausted = 5,
}

/// <summary>
/// Either candidates or a failure. Failures are returned, never thrown: an exception inside a
/// circuit event handler is the generic Blazor error UI, taking the untriaged batch with it.
/// </summary>
public record GenerationResult
{
    private GenerationResult(IReadOnlyList<CandidateCard>? candidates, GenerationFailure? failure, string? message)
    {
        Candidates = candidates;
        Failure = failure;
        Message = message;
    }

    public IReadOnlyList<CandidateCard>? Candidates { get; }

    public GenerationFailure? Failure { get; }

    /// <summary>Learner-facing; the page renders it.</summary>
    public string? Message { get; }

    public bool IsSuccess => Candidates is not null;

    public static GenerationResult Success(IReadOnlyList<CandidateCard> candidates) =>
        new(candidates, null, null);

    public static GenerationResult Failed(GenerationFailure failure, string message) =>
        new(null, failure, message);
}

/// <summary>
/// Liveness while streaming. Carries a count and nothing derived from the model's text — the page
/// renders this, and the passage is cleared before triage begins.
/// </summary>
public record GenerationProgress(int ChunksReceived);
