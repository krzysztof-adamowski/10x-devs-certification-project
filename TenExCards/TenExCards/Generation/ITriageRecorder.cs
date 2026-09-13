namespace TenExCards.Generation;

/// <summary>
/// The only type that touches the <c>TriageBatches</c> table. <c>ownerId</c> is required on every
/// member, like <c>ICardStore</c> — no ambient-user overload.
/// </summary>
/// <remarks>
/// Every member is best-effort: a fault is logged and swallowed, never surfaced. Measurement must
/// not cost a learner a card. The swallow lives in the implementation rather than at each call site
/// so a later caller is safe by construction (see lessons.md, "Put the validation gate on the call,
/// not on one route to it"). The cost is that the counts undercount under fault.
/// </remarks>
public interface ITriageRecorder
{
    /// <summary><c>null</c> when the row could not be written; the record methods then no-op.</summary>
    Task<Guid?> OpenBatchAsync(string ownerId, int candidateCount, CancellationToken ct);

    Task RecordAcceptAsync(string ownerId, Guid? batchId, bool edited, CancellationToken ct);

    Task RecordRejectAsync(string ownerId, Guid? batchId, CancellationToken ct);
}
