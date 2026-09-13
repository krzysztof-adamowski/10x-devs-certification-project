namespace TenExCards.Data;

/// <summary>
/// How long a card's prompt and answer may be. Deliberately <c>const</c>, not configuration: the
/// values are compiled into <c>AddCards</c>' <c>HasMaxLength</c>, so a setting could only widen the
/// gate in front of a column it cannot widen. Changing one means a migration.
/// </summary>
/// <remarks>
/// Three readers: <c>AppDbContext</c>'s <c>HasMaxLength</c>, <c>CandidateBounds</c>, and the
/// response schema in <c>GeminiCardCandidateGenerator</c>. <c>CandidateBoundsTests</c> pins these
/// against the literal widths <c>AddCards</c> wrote — not against each other, which would be a
/// tautology — because the EF in-memory provider ignores <c>HasMaxLength</c> and nothing else would
/// catch a drift. <c>const</c> also matters downstream: <c>S-05</c>'s <c>[MaxLength(...)]</c>
/// attributes need a compile-time constant.
/// </remarks>
public static class CardBounds
{
    public const int MaxPromptCharacters = 500;

    public const int MaxAnswerCharacters = 1_000;
}
