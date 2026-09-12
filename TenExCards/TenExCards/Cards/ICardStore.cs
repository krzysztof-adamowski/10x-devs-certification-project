using TenExCards.Data;

namespace TenExCards.Cards;

/// <summary>
/// The only type that touches the <c>Cards</c> table. The account boundary is a property of this
/// interface rather than of its callers: <paramref name="ownerId"/> is a required first parameter
/// on every member, there is no ambient-user overload, and there is no parameterless query.
/// </summary>
/// <remarks>
/// The owner id comes from the caller's authentication state, never from a form field or a route
/// value — a client-supplied owner id is the whole attack. <c>S-01</c> built the boundary and
/// <c>TenExCards.Tests/AGENTS.md</c> calls asserting it "the invariant this project exists to
/// protect"; <c>CardOwnershipTests</c> is this slice's share of that.
/// </remarks>
public interface ICardStore
{
    /// <summary>Persists one card for <paramref name="ownerId"/> and returns the stored row.</summary>
    Task<Card> SaveAsync(
        string ownerId,
        string prompt,
        string answer,
        CardOrigin origin,
        CancellationToken ct);

    /// <summary>Counts the cards owned by <paramref name="ownerId"/>, and no others.</summary>
    Task<int> CountForOwnerAsync(string ownerId, CancellationToken ct);
}
