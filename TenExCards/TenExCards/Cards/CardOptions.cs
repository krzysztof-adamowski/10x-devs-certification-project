namespace TenExCards.Cards;

/// <summary>
/// Bound from the <c>Cards</c> section. The card length bounds are deliberately NOT here — they are
/// <c>const</c> on <see cref="Data.CardBounds"/>, compiled into the migration's column widths.
/// </summary>
public class CardOptions
{
    public const string SectionName = "Cards";

    /// <summary>
    /// How many rows the recency list and a search each return. The cap is what keeps the
    /// saved-card surface a find surface rather than the browse surface the PRD rules out.
    /// </summary>
    public int MaxResults { get; set; } = 20;
}
