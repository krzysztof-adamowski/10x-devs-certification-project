using AwesomeAssertions;
using TenExCards.Generation;

namespace TenExCards.Tests;

public class PassageBoundsTests
{
    private static readonly GenerationOptions Options = new();

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("word", count));

    [Fact]
    public void IsWithinLimit_AtExactlyTheBound_IsAccepted()
    {
        var passage = new string('a', 12_000);

        PassageBounds.IsWithinLimit(passage, Options.MaxPassageCharacters).Should().BeTrue();
    }

    [Fact]
    public void IsWithinLimit_OneCharacterOver_IsRefused()
    {
        var passage = new string('a', 12_001);

        PassageBounds.IsWithinLimit(passage, Options.MaxPassageCharacters).Should().BeFalse(
            "the refusal must happen before generation begins, not after");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void IsWithinLimit_EmptyOrWhitespace_IsRefused(string passage)
    {
        PassageBounds.IsWithinLimit(passage, Options.MaxPassageCharacters).Should().BeFalse();
    }

    [Fact]
    public void TargetCandidateCount_ShortPassage_IsClampedToTheFloor()
    {
        PassageBounds.TargetCandidateCount(Words(20), Options).Should().Be(Options.MinCandidates);
    }

    [Fact]
    public void TargetCandidateCount_MidRangePassage_IsProportional()
    {
        PassageBounds.TargetCandidateCount(Words(1_000), Options).Should().Be(5);
    }

    [Fact]
    public void TargetCandidateCount_AtTheLongestAcceptedPassage_StaysBelowTheCap()
    {
        // The cap is unreachable from any passage the product accepts, so testing it with an
        // over-long passage would assert against an input that cannot occur.
        var longest = Words(2_000);
        longest.Length.Should().BeLessThanOrEqualTo(Options.MaxPassageCharacters);

        var target = PassageBounds.TargetCandidateCount(longest, Options);

        target.Should().Be(10);
        target.Should().BeLessThan(Options.MaxCandidates,
            "the clamp exists for a model that over-produces, not for a passage the product accepts");
    }

    [Fact]
    public void TargetCandidateCount_NeverExceedsTheCap()
    {
        PassageBounds.TargetCandidateCount(Words(100_000), Options).Should().Be(Options.MaxCandidates);
    }
}
