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
        CancellationToken ct);

    Task<int> CountForOwnerAsync(string ownerId, CancellationToken ct);
}
