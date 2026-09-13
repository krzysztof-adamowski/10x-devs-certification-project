namespace TenExCards.Data;

/// <summary>
/// One flashcard, owned by one account. There is deliberately no column for the source passage:
/// the PRD requires it to be unrecoverable once its candidates exist.
/// </summary>
public class Card
{
    public Guid Id { get; set; }

    /// <summary>Foreign key to <c>AspNetUsers.Id</c>. Every query filters on it.</summary>
    public string OwnerId { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public string Answer { get; set; } = string.Empty;

    public CardOrigin Origin { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Written as <see cref="Generated"/> here; <see cref="Manual"/> is what S-05 adds.</summary>
public enum CardOrigin
{
    Generated = 1,
    Manual = 2,
}
