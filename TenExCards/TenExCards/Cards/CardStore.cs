using Microsoft.EntityFrameworkCore;
using TenExCards.Data;

namespace TenExCards.Cards;

/// <summary>
/// The account-scoped implementation of <see cref="ICardStore"/>. Registered scoped in
/// <c>Program.cs</c>, alongside the scoped <see cref="AppDbContext"/> it takes.
/// </summary>
/// <remarks>
/// Every <c>Cards</c> query below filters on <c>OwnerId</c>. Removing one of those filters is the
/// regression <c>CardOwnershipTests</c> exists to catch — and that test has been observed failing
/// with the filter removed, which is the only thing that makes it a gate rather than decoration.
/// </remarks>
public class CardStore(AppDbContext db) : ICardStore
{
    public async Task<Card> SaveAsync(
        string ownerId,
        string prompt,
        string answer,
        CardOrigin origin,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        var card = new Card
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Prompt = prompt,
            Answer = answer,
            Origin = origin,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Cards.Add(card);
        await db.SaveChangesAsync(ct);
        return card;
    }

    public async Task<int> CountForOwnerAsync(string ownerId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        return await db.Cards.CountAsync(c => c.OwnerId == ownerId, ct);
    }
}
