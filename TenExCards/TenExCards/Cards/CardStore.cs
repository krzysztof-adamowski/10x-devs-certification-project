using Microsoft.EntityFrameworkCore;
using TenExCards.Data;

namespace TenExCards.Cards;

/// <summary>
/// Takes the factory rather than the scoped context: a DI scope in Blazor Server is the circuit,
/// so a scoped context here would live for the learner's whole session.
/// </summary>
public class CardStore(IDbContextFactory<AppDbContext> dbFactory) : ICardStore
{
    public async Task<Card> SaveAsync(
        string ownerId,
        string prompt,
        string answer,
        CardOrigin origin,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        // Length bounds are CandidateBounds' job; these catch the null that would otherwise throw
        // from inside a circuit event handler and take the untriaged batch with it.
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(answer);

        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "Not a defined CardOrigin.");
        }

        var card = new Card
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Prompt = prompt,
            Answer = answer,
            Origin = origin,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Cards.Add(card);
        await db.SaveChangesAsync(ct);
        return card;
    }

    public async Task<int> CountForOwnerAsync(string ownerId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Cards.CountAsync(c => c.OwnerId == ownerId, ct);
    }
}
