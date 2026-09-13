using TenExCards.Data;

namespace TenExCards.Cards;

/// <summary>
/// The only type that touches the <c>Cards</c> table. <c>ownerId</c> is required on every member —
/// no ambient-user overload, no parameterless query — and comes from the authentication state.
/// </summary>
public interface ICardStore
{
    Task<Card> SaveAsync(
        string ownerId,
        string prompt,
        string answer,
        CardOrigin origin,
        bool edited,
        CancellationToken ct);

    Task<int> CountForOwnerAsync(string ownerId, CancellationToken ct);

    /// <summary>
    /// Newest first. A null, empty or whitespace term means "most recent" — search and the default
    /// list are one query, so there is no second shape that could be written without the filter.
    /// </summary>
    Task<IReadOnlyList<Card>> FindForOwnerAsync(
        string ownerId,
        string? term,
        int limit,
        CancellationToken ct);

    Task<Card?> GetForOwnerAsync(string ownerId, Guid id, CancellationToken ct);

    /// <summary>
    /// <c>false</c> is "no row of yours matched" — not yours and already gone are deliberately
    /// indistinguishable, because separating them leaks another account's card existing.
    /// </summary>
    Task<bool> UpdateForOwnerAsync(
        string ownerId,
        Guid id,
        string prompt,
        string answer,
        CancellationToken ct);

    /// <inheritdoc cref="UpdateForOwnerAsync"/>
    Task<bool> DeleteForOwnerAsync(string ownerId, Guid id, CancellationToken ct);
}
