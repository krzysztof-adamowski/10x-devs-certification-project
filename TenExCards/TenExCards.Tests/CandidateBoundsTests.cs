using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenExCards.Data;
using TenExCards.Generation;

namespace TenExCards.Tests;

public class CandidateBoundsTests(TenExCardsWebApplicationFactory factory)
    : IClassFixture<TenExCardsWebApplicationFactory>
{
    private static CandidateCard Card(int promptLength, int answerLength) =>
        new(new string('p', promptLength), new string('a', answerLength));

    [Fact]
    public void WithinColumnLimits_AtExactlyTheLimits_AreKept()
    {
        var candidates = new List<CandidateCard> { Card(500, 1_000) };

        CandidateBounds.WithinColumnLimits(candidates).Should().HaveCount(1);
    }

    [Fact]
    public void WithinColumnLimits_PromptOneOver_IsDropped()
    {
        var candidates = new List<CandidateCard> { Card(501, 10) };

        CandidateBounds.WithinColumnLimits(candidates).Should().BeEmpty();
    }

    [Fact]
    public void WithinColumnLimits_AnswerOneOver_IsDropped()
    {
        var candidates = new List<CandidateCard> { Card(10, 1_001) };

        CandidateBounds.WithinColumnLimits(candidates).Should().BeEmpty();
    }

    [Fact]
    public void WithinColumnLimits_DropsOnlyTheOffenderAndPreservesOrder()
    {
        var good1 = new CandidateCard("First question?", "A");
        var bad = Card(501, 10);
        var good2 = new CandidateCard("Second question?", "B");

        var kept = CandidateBounds.WithinColumnLimits([good1, bad, good2]);

        kept.Should().Equal(good1, good2);
    }

    [Fact]
    public void WithinColumnLimits_DropsRatherThanTruncates()
    {
        var kept = CandidateBounds.WithinColumnLimits([Card(600, 10)]);

        kept.Should().BeEmpty();
        kept.Should().NotContain(c => c.Prompt.Length == CardBounds.MaxPromptCharacters);
    }

    [Theory]
    [InlineData(500, 1_000, true)]
    [InlineData(501, 1_000, false)]
    [InlineData(500, 1_001, false)]
    [InlineData(0, 0, true)]
    public void IsWithinColumnLimits_AtTheBoundaryInBothDirections(int promptLength, int answerLength, bool expected)
    {
        var within = CandidateBounds.IsWithinColumnLimits(
            new string('p', promptLength), new string('a', answerLength));

        within.Should().Be(expected);
    }

    [Fact]
    public void CardBounds_MatchTheWidthsTheMigrationWrote()
    {
        // The literals are the point: AddCards wrote nvarchar(500)/nvarchar(1000) to a live database,
        // and a const cannot be widened after the fact. Asserting against CardBounds instead would be
        // a tautology, since HasMaxLength reads the same constant.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var card = db.Model.FindEntityType(typeof(Card))!;
        var promptLength = card.FindProperty(nameof(Data.Card.Prompt))!.GetMaxLength();
        var answerLength = card.FindProperty(nameof(Data.Card.Answer))!.GetMaxLength();

        promptLength.Should().Be(500, "Card.Prompt is nvarchar(500) in the AddCards migration");
        answerLength.Should().Be(1_000, "Card.Answer is nvarchar(1000) in the AddCards migration");
    }
}
