namespace TenExCards.Generation;

/// <summary>
/// The last thing between a generated candidate and <c>SaveAsync</c>. The response schema states
/// the same bounds, but the pre-flight showed maxLength is accepted without being enforced.
/// </summary>
public static class CandidateBounds
{
    /// <summary>Drops over-long candidates, preserving order. Dropping, not truncating: a clipped
    /// card reads as a defect the learner cannot fix.</summary>
    public static IReadOnlyList<CandidateCard> WithinColumnLimits(
        IReadOnlyList<CandidateCard> candidates,
        GenerationOptions options)
    {
        var kept = new List<CandidateCard>(candidates.Count);

        foreach (var candidate in candidates)
        {
            if (candidate.Prompt.Length <= options.MaxPromptCharacters
                && candidate.Answer.Length <= options.MaxAnswerCharacters)
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }
}
