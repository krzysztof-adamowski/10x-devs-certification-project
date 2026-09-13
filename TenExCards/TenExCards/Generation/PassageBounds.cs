namespace TenExCards.Generation;

/// <summary>The length guard and the target-count computation.</summary>
public static class PassageBounds
{
    /// <summary>
    /// Consulted by the character counter, the submit control and the submit handler, so those
    /// three cannot disagree.
    /// </summary>
    public static bool IsWithinLimit(string passage, int maxCharacters) =>
        !string.IsNullOrWhiteSpace(passage) && passage.Length <= maxCharacters;

    /// <summary>
    /// One candidate per <see cref="GenerationOptions.WordsPerCandidate"/> words, clamped. At the
    /// 12,000-character bound this maxes at 10, so the upper clamp only catches over-production.
    /// </summary>
    public static int TargetCandidateCount(string passage, GenerationOptions options)
    {
        var words = CountWords(passage);
        var target = (int)Math.Ceiling((double)words / options.WordsPerCandidate);
        return Math.Clamp(target, options.MinCandidates, options.MaxCandidates);
    }

    private static int CountWords(string passage)
    {
        if (string.IsNullOrWhiteSpace(passage))
        {
            return 0;
        }

        var words = 0;
        var inWord = false;
        foreach (var c in passage)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                words++;
            }
        }

        return words;
    }
}
