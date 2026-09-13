namespace TenExCards.Data;

/// <summary>
/// One generation batch's triage outcome, owned by one account. Counts only: like <see cref="Card"/>
/// there is no column for anything the passage produced, so nothing here can reconstruct it.
/// Counters are mutated after the row is written, which is why the timestamp is OpenedAt.
/// </summary>
public class TriageBatch
{
    public Guid Id { get; set; }

    /// <summary>Foreign key to <c>AspNetUsers.Id</c>. Every query filters on it.</summary>
    public string OwnerId { get; set; } = string.Empty;

    public DateTimeOffset OpenedAt { get; set; }

    /// <summary>Batch size at open. Accepted + Rejected below it means the learner abandoned triage.</summary>
    public int CandidateCount { get; set; }

    public int AcceptedCount { get; set; }

    public int RejectedCount { get; set; }

    /// <summary>Of the accepted, how many the learner reworded first.</summary>
    public int EditedCount { get; set; }
}
