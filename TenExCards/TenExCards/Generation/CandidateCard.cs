namespace TenExCards.Generation;

/// <summary>A generated candidate, before triage. Only an accepted one becomes a <c>Card</c>.</summary>
public record CandidateCard(string Prompt, string Answer);
