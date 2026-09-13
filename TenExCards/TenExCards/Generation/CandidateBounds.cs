using TenExCards.Data;

namespace TenExCards.Generation;

/// <summary>
/// The last thing between a generated candidate and <c>SaveAsync</c>. The response schema states
/// the same bounds, but the pre-flight showed maxLength is accepted without being enforced.
/// </summary>
public static class CandidateBounds
{
    /// <summary>Drops over-long candidates, preserving order. Dropping, not truncating: a clipped
    /// card reads as a defect the learner cannot fix.</summary>
    public static IReadOnlyList<CandidateCard> WithinColumnLimits(IReadOnlyList<CandidateCard> candidates)
    {
        var kept = new List<CandidateCard>(candidates.Count);

        foreach (var candidate in candidates)
        {
            if (IsWithinColumnLimits(candidate.Prompt, candidate.Answer))
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }

    /// <summary>The same gate for one candidate, so the edit path cannot disagree with the
    /// generated path about what fits.</summary>
    public static bool IsWithinColumnLimits(string prompt, string answer) =>
        prompt.Length <= CardBounds.MaxPromptCharacters
        && answer.Length <= CardBounds.MaxAnswerCharacters;
}
