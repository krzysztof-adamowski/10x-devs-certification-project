namespace TenExCards.Data;

/// <summary>
/// A deliberately disposable row that exists only to prove the persistence spine reads and writes.
/// It carries no domain meaning and must never acquire any.
/// </summary>
/// <remarks>
/// This is the persistence counterpart to <c>Components/Pages/CircuitCheck.razor</c>: a
/// proof-of-life surface with a scheduled death. <c>S-01</c> (accounts-and-sessions) deletes this
/// entity, its table, the <c>/db-check</c> page and its nav entry, in the same way it deletes the
/// circuit check. No domain schema is designed in F-02 — identity tables arrive with <c>S-01</c>
/// and the card entity with <c>S-02</c>.
/// </remarks>
public class SpineProbe
{
    public int Id { get; set; }

    /// <summary>
    /// When the row was written, in UTC. Set by the application rather than by a database default,
    /// so the value proves the app wrote it rather than the server.
    /// </summary>
    public DateTime WrittenAtUtc { get; set; }
}
