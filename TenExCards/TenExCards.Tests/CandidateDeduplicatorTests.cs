using AwesomeAssertions;
using TenExCards.Generation;

namespace TenExCards.Tests;

public class CandidateDeduplicatorTests
{
    [Theory]
    [InlineData("What is a contained user?", "what is a contained user?")]
    [InlineData("What is a contained user?", "What is a contained user")]
    [InlineData("What is a contained user?", "  What   is a contained    user?  ")]
    [InlineData("What is a contained user?", "WHAT IS A CONTAINED USER...")]
    public void Deduplicate_PromptsDifferingOnlyInCasePunctuationOrSpacing_DropsTheLater(
        string first, string second)
    {
        var candidates = new List<CandidateCard>
        {
            new(first, "The first answer."),
            new(second, "A different answer."),
        };

        var kept = CandidateDeduplicator.Deduplicate(candidates);

        kept.Should().HaveCount(1);
        kept[0].Prompt.Should().Be(first, "the first occurrence is the one kept");
        kept[0].Answer.Should().Be("The first answer.");
    }

    [Fact]
    public void Deduplicate_DistinctPrompts_PreservesAllAndTheirOrder()
    {
        var candidates = new List<CandidateCard>
        {
            new("First question?", "A"),
            new("Second question?", "B"),
            new("Third question?", "C"),
        };

        var kept = CandidateDeduplicator.Deduplicate(candidates);

        kept.Select(c => c.Prompt).Should().Equal("First question?", "Second question?", "Third question?");
    }

    [Fact]
    public void Deduplicate_DuplicateInTheMiddle_PreservesTheOrderOfWhatSurvives()
    {
        var candidates = new List<CandidateCard>
        {
            new("First question?", "A"),
            new("Second question?", "B"),
            new("first question", "C"),
            new("Third question?", "D"),
        };

        var kept = CandidateDeduplicator.Deduplicate(candidates);

        kept.Select(c => c.Prompt).Should().Equal("First question?", "Second question?", "Third question?");
    }

    [Fact]
    public void Deduplicate_TwoPhrasingsOfTheSameClaim_AreBothKept()
    {
        // Asserted so the limit is not later mistaken for a bug.
        var candidates = new List<CandidateCard>
        {
            new("What does a contained user authenticate against?", "Its own database."),
            new("Against what does a contained database user authenticate?", "Its own database."),
        };

        CandidateDeduplicator.Deduplicate(candidates).Should().HaveCount(2);
    }

    [Fact]
    public void Deduplicate_EmptySet_ReturnsEmpty()
    {
        CandidateDeduplicator.Deduplicate([]).Should().BeEmpty();
    }
}
