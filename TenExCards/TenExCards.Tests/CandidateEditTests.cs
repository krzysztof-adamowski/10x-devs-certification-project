using AwesomeAssertions;
using TenExCards.Data;
using TenExCards.Generation;

namespace TenExCards.Tests;

/// <summary>The two decisions the edit path makes, which is all of it the HTTP harness can reach.</summary>
public class CandidateEditTests
{
    private static CandidateCard Original =>
        new("What does a fallback policy apply to?", "Every endpoint carrying no authorization metadata.");

    [Fact]
    public void WasEdited_WithIdenticalText_IsNotAnEdit()
    {
        CandidateEdit.WasEdited(Original, Original.Prompt, Original.Answer).Should().BeFalse();
    }

    [Theory]
    [InlineData("  ", "")]
    [InlineData("", "\t")]
    [InlineData("\n ", "  \n")]
    public void WasEdited_WithSurroundingWhitespaceOnly_IsNotAnEdit(string promptPad, string answerPad)
    {
        var edited = CandidateEdit.WasEdited(
            Original, promptPad + Original.Prompt + promptPad, answerPad + Original.Answer + answerPad);

        edited.Should().BeFalse("trimming happens on both sides, so spacing alone is not a correction");
    }

    [Fact]
    public void WasEdited_WithAChangedWordInThePrompt_IsAnEdit()
    {
        CandidateEdit.WasEdited(Original, "What does a fallback policy govern?", Original.Answer)
            .Should().BeTrue();
    }

    [Fact]
    public void WasEdited_WithAChangedWordInTheAnswer_IsAnEdit()
    {
        CandidateEdit.WasEdited(Original, Original.Prompt, "Every endpoint carrying no authorization attribute.")
            .Should().BeTrue();
    }

    [Fact]
    public void WasEdited_WithACaseChange_IsAnEdit()
    {
        CandidateEdit.WasEdited(Original, Original.Prompt.ToUpperInvariant(), Original.Answer)
            .Should().BeTrue("the comparison is ordinal, so case carries meaning");
    }

    [Theory]
    [InlineData("", "Answer")]
    [InlineData("   ", "Answer")]
    [InlineData("Prompt", "")]
    [InlineData("Prompt", "  \t ")]
    public void IsCommittable_WithAnEmptyField_IsRefused(string prompt, string answer)
    {
        CandidateEdit.IsCommittable(prompt, answer).Should().BeFalse();
    }

    [Fact]
    public void IsCommittable_AtExactlyTheBounds_IsAccepted()
    {
        var prompt = new string('p', CardBounds.MaxPromptCharacters);
        var answer = new string('a', CardBounds.MaxAnswerCharacters);

        CandidateEdit.IsCommittable(prompt, answer).Should().BeTrue();
    }

    [Fact]
    public void IsCommittable_OneCharacterOverThePromptBound_IsRefused()
    {
        var prompt = new string('p', CardBounds.MaxPromptCharacters + 1);

        CandidateEdit.IsCommittable(prompt, "Answer").Should().BeFalse();
    }

    [Fact]
    public void IsCommittable_OneCharacterOverTheAnswerBound_IsRefused()
    {
        var answer = new string('a', CardBounds.MaxAnswerCharacters + 1);

        CandidateEdit.IsCommittable("Prompt", answer).Should().BeFalse();
    }
}
