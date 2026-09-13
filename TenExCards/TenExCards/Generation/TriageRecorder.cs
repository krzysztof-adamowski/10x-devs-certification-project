using Microsoft.EntityFrameworkCore;
using TenExCards.Data;

namespace TenExCards.Generation;

/// <summary>
/// Takes the factory rather than the scoped context, for the reason <c>CardStore</c> does: a DI
/// scope in Blazor Server is the whole circuit.
/// </summary>
public class TriageRecorder(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<TriageRecorder> logger) : ITriageRecorder
{
    /// <summary>
    /// The shared factory is built with EnableRetryOnFailure(), whose default budget reaches ~30s.
    /// A catch alone would hide the fault only after the learner had already waited behind the
    /// _busy guard, on paths that show no progress. Bounding here, not at the call sites, so a
    /// later caller inherits it.
    /// </summary>
    private const int RecorderTimeoutSeconds = 2;

    public async Task<Guid?> OpenBatchAsync(string ownerId, int candidateCount, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        try
        {
            var batch = new TriageBatch
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                OpenedAt = DateTimeOffset.UtcNow,
                CandidateCount = candidateCount,
            };

            using var bounded = Bound(ct);
            await using var db = await dbFactory.CreateDbContextAsync(bounded.Token);
            db.TriageBatches.Add(batch);
            await db.SaveChangesAsync(bounded.Token);
            return batch.Id;
        }
        catch (Exception ex)
        {
            // OperationCanceledException from the bound lands here like any other fault.
            logger.LogWarning(ex, "Could not open a triage batch record; this batch goes unmeasured.");
            return null;
        }
    }

    public Task RecordAcceptAsync(string ownerId, Guid? batchId, bool edited, CancellationToken ct) =>
        IncrementAsync(ownerId, batchId, batch =>
        {
            batch.AcceptedCount++;
            if (edited)
            {
                batch.EditedCount++;
            }
        }, ct);

    public Task RecordRejectAsync(string ownerId, Guid? batchId, CancellationToken ct) =>
        IncrementAsync(ownerId, batchId, batch => batch.RejectedCount++, ct);

    private static CancellationTokenSource Bound(CancellationToken ct)
    {
        var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(TimeSpan.FromSeconds(RecorderTimeoutSeconds));
        return bounded;
    }

    /// <summary>
    /// Read-modify-write, not ExecuteUpdateAsync: the latter needs a relational provider and throws
    /// against the in-memory one the tests use. No lock — triage runs on one circuit dispatcher,
    /// serialised by the component's re-entrancy guard.
    /// </summary>
    private async Task IncrementAsync(
        string ownerId,
        Guid? batchId,
        Action<TriageBatch> apply,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        if (batchId is not { } id)
        {
            return;
        }

        try
        {
            using var bounded = Bound(ct);
            await using var db = await dbFactory.CreateDbContextAsync(bounded.Token);
            // Scoped by owner as well as id, so a batch id that is not yours moves nothing.
            var batch = await db.TriageBatches
                .SingleOrDefaultAsync(b => b.Id == id && b.OwnerId == ownerId, bounded.Token);
            if (batch is null)
            {
                return;
            }

            apply(batch);
            await db.SaveChangesAsync(bounded.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record a triage outcome on batch {BatchId}.", id);
        }
    }
}
