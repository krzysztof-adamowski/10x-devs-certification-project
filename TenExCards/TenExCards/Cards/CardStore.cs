using Microsoft.EntityFrameworkCore;
using TenExCards.Data;

namespace TenExCards.Cards;

/// <summary>
/// The account-scoped implementation of <see cref="ICardStore"/>. Registered scoped in
/// <c>Program.cs</c>, but it creates and disposes its own <see cref="AppDbContext"/> per call.
/// </summary>
/// <remarks>
/// <para>
/// Every <c>Cards</c> query below filters on <c>OwnerId</c>. Removing one of those filters is the
/// regression <c>CardOwnershipTests</c> exists to catch — and that test has been observed failing
/// with the filter removed, which is the only thing that makes it a gate rather than decoration.
/// </para>
/// <para>
/// IT TAKES THE FACTORY, NOT THE SCOPED CONTEXT, AND THAT IS NOT A STYLE CHOICE. In Blazor Server
/// a DI scope is the CIRCUIT, not a request, so a scoped <see cref="AppDbContext"/> injected here
/// would live for the learner's whole session. Two things then go wrong in exactly the slice that
/// saves many cards per circuit: the change tracker accumulates every saved <see cref="Card"/>
/// until the circuit ends, against AGENTS.md's "never hold more in a circuit than you must"; and
/// two overlapping component events on one context throw "A second operation was started on this
/// context instance", which inside a circuit event handler is the generic Blazor error UI with the
/// untriaged batch lost behind it. A context per call costs a pooled connection and removes both.
/// </para>
/// </remarks>
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

        // Blank content only — the 500/1,000 LENGTH bounds deliberately live in exactly one place,
        // S-02's CandidateBounds, because a second copy of those two numbers is the drift the plan
        // forbids. What this catches is the null that would otherwise surface as a DbUpdateException
        // on the NOT NULL constraint, inside a circuit event handler, taking the batch with it.
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(answer);

        // default(CardOrigin) is 0, which names no member — the explicit values on the enum stop a
        // reorder from remapping rows, but nothing stops an unset value from being written as one.
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
