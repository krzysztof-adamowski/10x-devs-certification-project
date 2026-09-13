using AwesomeAssertions;
using TenExCards.Generation;

namespace TenExCards.Tests;

/// <summary>
/// The fourth invariant in TenExCards.Tests/AGENTS.md: each candidate is triaged exactly once.
/// S-02's review found a re-entrancy hole that violated it; the guard stayed unasserted until the
/// counters moved out of the component.
/// </summary>
public class TriageSessionTests
{
    private static TriageSession SessionOf(int n) =>
        new(Enumerable.Range(0, n).Select(i => new CandidateCard($"Prompt {i}", $"Answer {i}")));

    [Theory]
    [InlineData("AAA")]
    [InlineData("RRR")]
    [InlineData("ARA")]
    [InlineData("RAAR")]
    [InlineData("AREAR")]
    public void AnySequenceOfDecisions_TriagesEachCandidateExactlyOnce(string decisions)
    {
        var session = SessionOf(decisions.Length);
        var seen = new List<string>();

        foreach (var decision in decisions)
        {
            seen.Add(session.Current.Prompt);

            // 'E' is an edit-then-accept: the same advance, recorded differently.
            _ = decision switch
            {
                'A' => session.Accept(edited: false),
                'E' => session.Accept(edited: true),
                _ => session.Reject(),
            };
        }

        seen.Should().OnlyHaveUniqueItems("no candidate may be decided twice");
        seen.Should().HaveCount(decisions.Length, "none may be skipped");
        session.IsComplete.Should().BeTrue();
        (session.Saved + session.Discarded).Should().Be(decisions.Length);
        session.Saved.Should().Be(decisions.Count(c => c is 'A' or 'E'));
        session.Discarded.Should().Be(decisions.Count(c => c is 'R'));
        session.EditedCount.Should().Be(decisions.Count(c => c is 'E'));
    }

    [Fact]
    public void ARepeatedDecisionPastTheEnd_IsRefusedAndChangesNothing()
    {
        var session = SessionOf(2);
        session.Accept(edited: false).Should().BeTrue();
        session.Reject().Should().BeTrue();

        session.Accept(edited: true).Should().BeFalse("the batch is empty; this is the re-entrancy shape");
        session.Reject().Should().BeFalse();

        session.Saved.Should().Be(1);
        session.Discarded.Should().Be(1);
        session.EditedCount.Should().Be(0);
        session.TriagedCount.Should().Be(session.BatchSize);
    }

    [Fact]
    public void Position_CountsFromOneAndTracksTheDecisionsMade()
    {
        var session = SessionOf(3);

        session.Position.Should().Be(1);
        session.Accept(edited: false);
        session.Position.Should().Be(2);
        session.Reject();
        session.Position.Should().Be(3);
    }

    [Fact]
    public void Current_IsTheNextUndecidedCandidateInOrder()
    {
        var session = SessionOf(3);

        session.Current.Prompt.Should().Be("Prompt 0");
        session.Accept(edited: false);
        session.Current.Prompt.Should().Be("Prompt 1");
        session.Reject();
        session.Current.Prompt.Should().Be("Prompt 2");
    }
}
