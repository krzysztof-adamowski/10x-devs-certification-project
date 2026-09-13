using AwesomeAssertions;
using TenExCards.Generation;

namespace TenExCards.Tests;

/// <summary>Pins the four quality properties so the prompt cannot be quietly softened.</summary>
public class CardGenerationPromptTests
{
    private static readonly string Prompt = CardGenerationPrompt.System;

    [Fact]
    public void System_StatesTheOneLoadBearingClaimProperty()
    {
        Prompt.Should().ContainEquivalentOf("one load-bearing claim");
        Prompt.Should().ContainEquivalentOf("exactly one fact");
    }

    [Fact]
    public void System_StatesTheReformulationProperty()
    {
        Prompt.Should().ContainEquivalentOf("reformulated");
        Prompt.Should().ContainEquivalentOf("different words from the source");
    }

    [Fact]
    public void System_StatesTheSingleDefensibleAnswerProperty()
    {
        Prompt.Should().ContainEquivalentOf("exactly one defensible answer");
    }

    [Fact]
    public void System_StatesTheNoDuplicateClaimProperty()
    {
        Prompt.Should().ContainEquivalentOf("no two cards test the same claim");
    }

    [Fact]
    public void System_SaysWhatCountsAsLoadBearing()
    {
        Prompt.Should().ContainEquivalentOf("definitions");
        Prompt.Should().ContainEquivalentOf("causal links");
        Prompt.Should().ContainEquivalentOf("distinctions");
        Prompt.Should().ContainEquivalentOf("incidental dates, names or examples");
    }

    [Fact]
    public void System_SaysThePassageIsSeenOnce()
    {
        Prompt.Should().ContainEquivalentOf("one pass");
        Prompt.Should().ContainEquivalentOf("cannot consult it again");
    }

    [Fact]
    public void System_SaysTheFocusHintNarrowsWhatGetsCarded()
    {
        Prompt.Should().ContainEquivalentOf("focus hint");
        Prompt.Should().ContainEquivalentOf("narrows what gets carded");
    }
}
