namespace TenExCards.Generation;

/// <summary>
/// One batch being triaged, one candidate at a time. Holds no I/O — the component keeps SaveAsync,
/// the interop and the re-entrancy guard — so the "each candidate is triaged exactly once"
/// invariant can be asserted without a component framework.
/// </summary>
public sealed class TriageSession
{
    private readonly List<CandidateCard> _pending;

    public TriageSession(IEnumerable<CandidateCard> candidates)
    {
        _pending = [.. candidates];
        BatchSize = _pending.Count;
    }

    public int BatchSize { get; }

    public int Saved { get; private set; }

    public int Discarded { get; private set; }

    /// <summary>Of the saved cards, how many the learner reworded. Display is S-06's.</summary>
    public int EditedCount { get; private set; }

    public int TriagedCount => Saved + Discarded;

    public bool IsComplete => _pending.Count == 0;

    public CandidateCard Current => _pending[0];

    /// <summary>One-based, for "Card 2 of 5".</summary>
    public int Position => TriagedCount + 1;

    /// <summary>Called only after the save succeeded; a failed save must leave the batch untouched
    /// so a retry re-issues the same save rather than queueing a second one.</summary>
    public bool Accept(bool edited)
    {
        if (IsComplete)
        {
            return false;
        }

        Saved++;
        if (edited)
        {
            EditedCount++;
        }

        _pending.RemoveAt(0);
        return true;
    }

    public bool Reject()
    {
        if (IsComplete)
        {
            return false;
        }

        Discarded++;
        _pending.RemoveAt(0);
        return true;
    }
}
