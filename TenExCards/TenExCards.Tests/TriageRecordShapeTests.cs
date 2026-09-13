using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenExCards.Data;

namespace TenExCards.Tests;

/// <summary>
/// The triage record carries counts and nothing a passage produced.
/// </summary>
/// <remarks>
/// Asserted as set EQUALITY, not "does not contain a text column". lessons.md records an absence
/// check that answered 0 both for an absent type and a present one — a broken read returning the
/// desired answer. Equality cannot pass vacuously: a model read that returns nothing fails it. The
/// Card test below is the control that the read itself works.
/// </remarks>
public class TriageRecordShapeTests(TenExCardsWebApplicationFactory factory)
    : IClassFixture<TenExCardsWebApplicationFactory>
{
    private IReadOnlyList<string> MappedPropertyNames<T>()
    {
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = dbFactory.CreateDbContext();
        var entity = db.Model.FindEntityType(typeof(T));
        entity.Should().NotBeNull($"{typeof(T).Name} must be mapped");
        return [.. entity!.GetProperties().Select(p => p.Name)];
    }

    [Fact]
    public void TriageBatch_MapsExactlyTheCountingColumns()
    {
        MappedPropertyNames<TriageBatch>().Should().BeEquivalentTo(
        [
            nameof(TriageBatch.Id),
            nameof(TriageBatch.OwnerId),
            nameof(TriageBatch.OpenedAt),
            nameof(TriageBatch.CandidateCount),
            nameof(TriageBatch.AcceptedCount),
            nameof(TriageBatch.RejectedCount),
            nameof(TriageBatch.EditedCount),
        ], "a column added here is a column the passage could be reconstructed from");
    }

    [Fact]
    public void Card_MapsItsKnownColumns_TheControlThatThisReadWorks()
    {
        MappedPropertyNames<Card>().Should().BeEquivalentTo(
        [
            nameof(Card.Id),
            nameof(Card.OwnerId),
            nameof(Card.Prompt),
            nameof(Card.Answer),
            nameof(Card.Origin),
            nameof(Card.Edited),
            nameof(Card.CreatedAt),
        ]);
    }
}
