using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TenExCards.Data;
using TenExCards.Generation;

namespace TenExCards.Tests;

public class CandidateBoundsTests(TenExCardsWebApplicationFactory factory)
    : IClassFixture<TenExCardsWebApplicationFactory>
{
    private static readonly GenerationOptions Options = new();

    private static CandidateCard Card(int promptLength, int answerLength) =>
        new(new string('p', promptLength), new string('a', answerLength));

    [Fact]
    public void WithinColumnLimits_AtExactlyTheLimits_AreKept()
    {
        var candidates = new List<CandidateCard> { Card(500, 1_000) };

        CandidateBounds.WithinColumnLimits(candidates, Options).Should().HaveCount(1);
    }

    [Fact]
    public void WithinColumnLimits_PromptOneOver_IsDropped()
    {
        var candidates = new List<CandidateCard> { Card(501, 10) };

        CandidateBounds.WithinColumnLimits(candidates, Options).Should().BeEmpty();
    }

    [Fact]
    public void WithinColumnLimits_AnswerOneOver_IsDropped()
    {
        var candidates = new List<CandidateCard> { Card(10, 1_001) };

        CandidateBounds.WithinColumnLimits(candidates, Options).Should().BeEmpty();
    }

    [Fact]
    public void WithinColumnLimits_DropsOnlyTheOffenderAndPreservesOrder()
    {
        var good1 = new CandidateCard("First question?", "A");
        var bad = Card(501, 10);
        var good2 = new CandidateCard("Second question?", "B");

        var kept = CandidateBounds.WithinColumnLimits([good1, bad, good2], Options);

        kept.Should().Equal(good1, good2);
    }

    [Fact]
    public void WithinColumnLimits_DropsRatherThanTruncates()
    {
        var kept = CandidateBounds.WithinColumnLimits([Card(600, 10)], Options);

        kept.Should().BeEmpty();
        kept.Should().NotContain(c => c.Prompt.Length == Options.MaxPromptCharacters);
    }

    [Fact]
    public void ConfiguredLimits_MatchTheEntitysColumnLengths()
    {
        // Nothing else catches this: the in-memory provider ignores HasMaxLength.
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<GenerationOptions>>().Value;
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var card = db.Model.FindEntityType(typeof(Card))!;
        var promptLength = card.FindProperty(nameof(Data.Card.Prompt))!.GetMaxLength();
        var answerLength = card.FindProperty(nameof(Data.Card.Answer))!.GetMaxLength();

        promptLength.Should().Be(options.MaxPromptCharacters,
            "Generation:MaxPromptCharacters and Card.Prompt's HasMaxLength are one number in two places");
        answerLength.Should().Be(options.MaxAnswerCharacters,
            "Generation:MaxAnswerCharacters and Card.Answer's HasMaxLength are one number in two places");
    }

    [Fact]
    public void ClassDefaults_MatchTheConfiguredValues()
    {
        using var scope = factory.Services.CreateScope();
        var configured = scope.ServiceProvider.GetRequiredService<IOptions<GenerationOptions>>().Value;

        Options.MaxPromptCharacters.Should().Be(configured.MaxPromptCharacters);
        Options.MaxAnswerCharacters.Should().Be(configured.MaxAnswerCharacters);
    }
}
