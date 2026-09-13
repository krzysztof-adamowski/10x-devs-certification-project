using AwesomeAssertions;
using TenExCards.Cards;
using TenExCards.Data;

namespace TenExCards.Tests;

/// <summary>The edit rule, asserted without rendering the page that calls it.</summary>
public class CardEditTests
{
    private static string OfLength(int length) => new('x', length);

    [Fact]
    public void Validate_AtExactlyTheLimits_IsAccepted()
    {
        var result = CardEdit.Validate(
            OfLength(CardBounds.MaxPromptCharacters),
            OfLength(CardBounds.MaxAnswerCharacters));

        result.IsValid.Should().BeTrue();
        result.Message.Should().BeNull();
    }

    [Fact]
    public void Validate_PromptOneOver_IsRefusedNamingThePrompt()
    {
        var result = CardEdit.Validate(OfLength(CardBounds.MaxPromptCharacters + 1), "An answer.");

        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("prompt");
    }

    [Fact]
    public void Validate_AnswerOneOver_IsRefusedNamingTheAnswer()
    {
        var result = CardEdit.Validate("A prompt?", OfLength(CardBounds.MaxAnswerCharacters + 1));

        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("answer");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_EmptyPrompt_IsRefused(string? prompt)
    {
        var result = CardEdit.Validate(prompt, "An answer.");

        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("prompt");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_EmptyAnswer_IsRefused(string? answer)
    {
        var result = CardEdit.Validate("A prompt?", answer);

        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("answer");
    }

    [Fact]
    public void Validate_OnSuccess_ReturnsTrimmedText()
    {
        var result = CardEdit.Validate("  A prompt?  ", "\tAn answer.\n");

        result.IsValid.Should().BeTrue();
        result.Prompt.Should().Be("A prompt?");
        result.Answer.Should().Be("An answer.");
    }

    [Theory]
    [InlineData("A prompt?", "An answer.")]
    [InlineData("  A prompt?  ", "  An answer.  ")]
    [InlineData("", "An answer.")]
    [InlineData("   ", "An answer.")]
    [InlineData("A prompt?", "   ")]
    public void IsCommittable_AgreesWithValidate_IncludingOnUntrimmedInput(string prompt, string answer)
    {
        // The triage handler branches on IsCommittable and renders Validate's message. When those
        // were two rules they disagreed on trimming, and a refusal could carry a null message.
        Generation.CandidateEdit.IsCommittable(prompt, answer)
            .Should().Be(CardEdit.Validate(prompt, answer).IsValid);
    }

    [Fact]
    public void IsCommittable_AtTheLimitPlusTrailingWhitespace_AgreesWithValidate()
    {
        // The case that used to diverge: untrimmed length is over, trimmed length is not.
        var prompt = OfLength(CardBounds.MaxPromptCharacters) + "   ";

        Generation.CandidateEdit.IsCommittable(prompt, "An answer.").Should().BeTrue();
        CardEdit.Validate(prompt, "An answer.").IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AtTheLimitPlusTrailingWhitespace_IsAccepted()
    {
        // Trimming happens before measuring, so whitespace never costs a character of the limit.
        var result = CardEdit.Validate(
            OfLength(CardBounds.MaxPromptCharacters) + "   ",
            OfLength(CardBounds.MaxAnswerCharacters) + "\n");

        result.IsValid.Should().BeTrue();
        result.Prompt!.Length.Should().Be(CardBounds.MaxPromptCharacters);
        result.Answer!.Length.Should().Be(CardBounds.MaxAnswerCharacters);
    }
}
