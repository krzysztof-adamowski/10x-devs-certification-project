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
        bool edited,
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
            Edited = edited,
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

    public async Task<IReadOnlyList<Card>> FindForOwnerAsync(
        string ownerId,
        string? term,
        int limit,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        // A misconfigured MaxResults must not silently return nothing.
        limit = Math.Max(1, limit);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Cards.Where(c => c.OwnerId == ownerId);

        if (!string.IsNullOrWhiteSpace(term))
        {
            // Both sides lowered in the query, rather than leaning on the database collation: the
            // in-memory provider the tests use is case-sensitive and Azure SQL's default is not, so
            // this is what makes the suite assert what production does.
            var needle = term.Trim().ToLower();
            query = query.Where(c =>
                c.Prompt.ToLower().Contains(needle) || c.Answer.ToLower().Contains(needle));
        }

        // Id breaks the tie: CreatedAt comes from DateTimeOffset.UtcNow per save, so two cards from
        // one batch can share it, and an unordered tie at the cutoff hides a card at random.
        return await query
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<Card?> GetForOwnerAsync(string ownerId, Guid id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Cards.SingleOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId, ct);
    }

    public async Task<bool> UpdateForOwnerAsync(
        string ownerId,
        Guid id,
        string prompt,
        string answer,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(answer);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var card = await db.Cards.SingleOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId, ct);
        if (card is null)
        {
            return false;
        }

        // Origin and Edited are untouched on purpose: they record how the card was CREATED and
        // whether it was edited before saving, which S-06 reads. A later repair is neither.
        card.Prompt = prompt;
        card.Answer = answer;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteForOwnerAsync(string ownerId, Guid id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var card = await db.Cards.SingleOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId, ct);
        if (card is null)
        {
            return false;
        }

        db.Cards.Remove(card);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
