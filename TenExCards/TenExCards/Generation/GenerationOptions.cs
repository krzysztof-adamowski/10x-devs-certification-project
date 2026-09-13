namespace TenExCards.Generation;

/// <summary>Tunable numbers behind generation, bound from the <c>Generation</c> section.</summary>
public class GenerationOptions
{
    public const string SectionName = "Generation";

    public int MaxPassageCharacters { get; set; } = 12_000;

    public int MaxFocusHintCharacters { get; set; } = 200;

    /// <summary>Below this, the result is a <see cref="GenerationFailure.Refused"/> failure.</summary>
    public int MinCandidates { get; set; } = 3;

    /// <summary>A clip for a model that over-produces; the computation itself maxes at 10.</summary>
    public int MaxCandidates { get; set; } = 12;

    public int WordsPerCandidate { get; set; } = 200;

    // These two mirror Card's HasMaxLength. CandidateBoundsTests asserts the equality, because the
    // EF in-memory provider ignores HasMaxLength and nothing else would catch a drift.
    public int MaxPromptCharacters { get; set; } = 500;

    public int MaxAnswerCharacters { get; set; } = 1_000;

    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>Which models answer, and where. Bound from the <c>Gemini</c> section.</summary>
public class GeminiOptions
{
    public const string SectionName = "Gemini";

    /// <summary>
    /// Tried in order, falling through on a free-tier daily quota exhaustion (HTTP 429). Best
    /// quality first: the later entries have far larger quotas but weaker output, so the app
    /// degrades rather than failing once the preferred model is spent.
    /// </summary>
    // Empty default, not a seeded one: the configuration binder APPENDS to an array property's
    // existing contents, so a default here would survive binding and duplicate a configured entry.
    // Program.cs validates this eagerly instead.
    public string[] Models { get; set; } = [];

    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai/";

    /// <summary>From the app setting or user-secrets. Never <c>appsettings.json</c>.</summary>
    public string? ApiKey { get; set; }
}
