using TenExCards.Cards;

namespace TenExCards.Generation;

/// <summary>
/// The two decisions the edit path makes. Pure functions rather than component state, because a
/// @rendermode InteractiveServer component cannot be driven by the HTTP test harness.
/// </summary>
public static class CandidateEdit
{
    /// <summary>
    /// Whether committed text differs from what was generated. Ordinal, not culture-sensitive:
    /// culture comparison treats some genuinely different strings as equal. Trimmed on both sides,
    /// so whitespace alone is not an edit — a case change is.
    /// </summary>
    public static bool WasEdited(CandidateCard original, string prompt, string answer) =>
        !string.Equals(original.Prompt.Trim(), prompt.Trim(), StringComparison.Ordinal)
        || !string.Equals(original.Answer.Trim(), answer.Trim(), StringComparison.Ordinal);

    /// <summary>
    /// The single predicate the disabled attribute and the click handler both consult, so they
    /// cannot drift. Delegates to <see cref="CardEdit"/> so the decision and the refusal message
    /// the handler renders come from one rule — they were two, and disagreed on trimming, which
    /// would have surfaced as a refusal with no message.
    /// </summary>
    public static bool IsCommittable(string prompt, string answer) =>
        CardEdit.Validate(prompt, answer).IsValid;
}
