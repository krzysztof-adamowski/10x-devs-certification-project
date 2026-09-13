using System.Text;

namespace TenExCards.Generation;

/// <summary>
/// Collapses prompts that differ only in case, spacing or trailing punctuation. Two phrasings of
/// the same claim survive on purpose — that judgement belongs to the prompt and to the learner.
/// </summary>
public static class CandidateDeduplicator
{
    public static IReadOnlyList<CandidateCard> Deduplicate(IReadOnlyList<CandidateCard> candidates)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<CandidateCard>(candidates.Count);

        foreach (var candidate in candidates)
        {
            if (seen.Add(Normalise(candidate.Prompt)))
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }

    private static string Normalise(string prompt)
    {
        var builder = new StringBuilder(prompt.Length);
        var pendingSpace = false;

        foreach (var c in prompt.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        while (builder.Length > 0 && char.IsPunctuation(builder[^1]))
        {
            builder.Length--;
        }

        return builder.ToString();
    }
}
