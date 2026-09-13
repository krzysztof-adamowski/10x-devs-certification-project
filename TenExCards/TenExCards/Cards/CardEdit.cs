using TenExCards.Data;

namespace TenExCards.Cards;

/// <summary>Either the trimmed replacement text, or the reason it was refused.</summary>
public record CardEditResult
{
    private CardEditResult(string? prompt, string? answer, string? message)
    {
        Prompt = prompt;
        Answer = answer;
        Message = message;
    }

    public string? Prompt { get; }

    public string? Answer { get; }

    /// <summary>Learner-facing; names the field that failed.</summary>
    public string? Message { get; }

    public bool IsValid => Message is null;

    public static CardEditResult Valid(string prompt, string answer) => new(prompt, answer, null);

    public static CardEditResult Refused(string message) => new(null, null, message);
}

/// <summary>
/// What the learner typed into an edit form, judged without rendering anything. Bounds come from
/// <see cref="CardBounds"/>, so the gate cannot disagree with the column it stands in front of.
/// </summary>
public static class CardEdit
{
    /// <summary>
    /// Trims first, then measures: trailing whitespace must not cost a character of the limit.
    /// </summary>
    public static CardEditResult Validate(string? prompt, string? answer)
    {
        var trimmedPrompt = prompt?.Trim() ?? string.Empty;
        var trimmedAnswer = answer?.Trim() ?? string.Empty;

        if (trimmedPrompt.Length == 0)
        {
            return CardEditResult.Refused("The prompt cannot be empty.");
        }

        if (trimmedAnswer.Length == 0)
        {
            return CardEditResult.Refused("The answer cannot be empty.");
        }

        if (trimmedPrompt.Length > CardBounds.MaxPromptCharacters)
        {
            return CardEditResult.Refused(
                $"That prompt is {trimmedPrompt.Length:N0} characters. The limit is {CardBounds.MaxPromptCharacters:N0}.");
        }

        if (trimmedAnswer.Length > CardBounds.MaxAnswerCharacters)
        {
            return CardEditResult.Refused(
                $"That answer is {trimmedAnswer.Length:N0} characters. The limit is {CardBounds.MaxAnswerCharacters:N0}.");
        }

        return CardEditResult.Valid(trimmedPrompt, trimmedAnswer);
    }
}
