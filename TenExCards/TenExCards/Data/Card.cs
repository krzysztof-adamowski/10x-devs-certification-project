namespace TenExCards.Data;

/// <summary>
/// The product's first persisted entity: one flashcard, owned by exactly one account.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no column for the passage a card came from. The PRD requires a submitted
/// passage to be unrecoverable once its candidates exist, and the cheapest way to guarantee that is
/// to leave nowhere for it to go — see <c>TenExCards/AGENTS.md</c>, "Never persist a submitted
/// passage".
/// </para>
/// <para>
/// <see cref="Origin"/> is written from the start rather than added by <c>S-05</c>, because
/// migrations here run on the boot path and are forward-only: backfilling a discriminator against
/// live rows later is a one-way step this slice can avoid for the cost of one column.
/// </para>
/// </remarks>
public class Card
{
    public Guid Id { get; set; }

    /// <summary>
    /// Foreign key to <c>AspNetUsers.Id</c>. Typed <see cref="string"/> to match
    /// <see cref="Microsoft.AspNetCore.Identity.IdentityUser.Id"/>, which Identity keys as a string
    /// even though it stores a GUID in it. Every query against <see cref="AppDbContext.Cards"/>
    /// filters on this — see <c>TenExCards.Cards.CardStore</c>, which is the only type that may.
    /// </summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>The question side. Bounded at 500 characters by <see cref="AppDbContext"/>.</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>The answer side. Bounded at 1,000 characters by <see cref="AppDbContext"/>.</summary>
    public string Answer { get; set; } = string.Empty;

    public CardOrigin Origin { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// How a card came to exist. Only <see cref="Generated"/> is written by <c>S-02</c>;
/// <see cref="Manual"/> is what makes <c>S-05</c> additive.
/// </summary>
/// <remarks>
/// The numeric values are EXPLICIT ON PURPOSE. This enum is stored as an <c>int</c>, so reordering
/// or inserting a member without them would silently remap every existing row — a data corruption
/// no migration and no test would report.
/// </remarks>
public enum CardOrigin
{
    Generated = 1,
    Manual = 2,
}
