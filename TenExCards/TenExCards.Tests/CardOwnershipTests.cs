using AwesomeAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenExCards.Cards;
using TenExCards.Data;

namespace TenExCards.Tests;

/// <summary>The account boundary around <c>Card</c>.</summary>
public class CardOwnershipTests(TenExCardsWebApplicationFactory factory)
    : IClassFixture<TenExCardsWebApplicationFactory>
{
    private const string ValidPassword = "CorrectHorseBattery16";

    private async Task<string> CreateUserAsync()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        // One address for both: the unique UserNameIndex is the only database-level guard on
        // email uniqueness.
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser { UserName = email, Email = email };
        var result = await userManager.CreateAsync(user, ValidPassword);
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Description)));
        return user.Id;
    }

    private async Task<T> WithStoreAsync<T>(Func<ICardStore, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ICardStore>());
    }

    [Fact]
    public async Task SaveAsync_ThenCountForAnotherOwner_ReturnsNothing()
    {
        var ownerA = await CreateUserAsync();
        var ownerB = await CreateUserAsync();

        await WithStoreAsync(store => store.SaveAsync(
            ownerA, "What does a contained database user authenticate against?",
            "The database named in its own connection string.", CardOrigin.Generated, edited: false, CancellationToken.None));

        var seenByOwner = await WithStoreAsync(store =>
            store.CountForOwnerAsync(ownerA, CancellationToken.None));
        var seenByOther = await WithStoreAsync(store =>
            store.CountForOwnerAsync(ownerB, CancellationToken.None));

        seenByOwner.Should().Be(1);
        seenByOther.Should().Be(0, "a card saved for one account must be invisible to every other");
    }

    [Fact]
    public async Task CountForOwnerAsync_WithRowsFromSeveralOwners_CountsOnlyTheCallers()
    {
        var ownerA = await CreateUserAsync();
        var ownerB = await CreateUserAsync();

        for (var i = 0; i < 3; i++)
        {
            await WithStoreAsync(store => store.SaveAsync(
                ownerA, $"Prompt A{i}", $"Answer A{i}", CardOrigin.Generated, edited: false, CancellationToken.None));
        }

        await WithStoreAsync(store => store.SaveAsync(
            ownerB, "Prompt B0", "Answer B0", CardOrigin.Generated, edited: false, CancellationToken.None));

        (await WithStoreAsync(store => store.CountForOwnerAsync(ownerA, CancellationToken.None)))
            .Should().Be(3);
        (await WithStoreAsync(store => store.CountForOwnerAsync(ownerB, CancellationToken.None)))
            .Should().Be(1);
    }

    [Fact]
    public async Task SaveAsync_PersistsOwnerOriginAndCreatedAt()
    {
        var ownerA = await CreateUserAsync();
        var before = DateTimeOffset.UtcNow;

        var saved = await WithStoreAsync(store => store.SaveAsync(
            ownerA, "Which mode of az deployment group create deletes absent resources?",
            "Complete.", CardOrigin.Generated, edited: false, CancellationToken.None));

        saved.OwnerId.Should().Be(ownerA);
        saved.Origin.Should().Be(CardOrigin.Generated);
        saved.CreatedAt.Should().NotBe(default);
        saved.CreatedAt.Should().BeOnOrAfter(before);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Cards.SingleAsync(c => c.Id == saved.Id, CancellationToken.None);

        stored.OwnerId.Should().Be(ownerA);
        stored.Origin.Should().Be(CardOrigin.Generated);
        stored.Prompt.Should().Be(saved.Prompt);
        stored.Answer.Should().Be(saved.Answer);
    }

    [Fact]
    public async Task SaveAsync_RoundTripsTheEditedFlag()
    {
        var ownerA = await CreateUserAsync();

        var editedCard = await WithStoreAsync(store => store.SaveAsync(
            ownerA, "Prompt the learner reworded", "Answer the learner reworded",
            CardOrigin.Generated, edited: true, CancellationToken.None));

        var untouchedCard = await WithStoreAsync(store => store.SaveAsync(
            ownerA, "Prompt accepted as generated", "Answer accepted as generated",
            CardOrigin.Generated, edited: false, CancellationToken.None));

        editedCard.Edited.Should().BeTrue();
        untouchedCard.Edited.Should().BeFalse();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var storedEdited = await db.Cards
            .SingleAsync(c => c.OwnerId == ownerA && c.Id == editedCard.Id, CancellationToken.None);
        var storedUntouched = await db.Cards
            .SingleAsync(c => c.OwnerId == ownerA && c.Id == untouchedCard.Id, CancellationToken.None);

        storedEdited.Edited.Should().BeTrue("S-06 cannot backfill what the store dropped");
        storedUntouched.Edited.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveAsync_WithNoOwner_IsRefused(string ownerId)
    {
        var act = async () => await WithStoreAsync(store => store.SaveAsync(
            ownerId, "Prompt", "Answer", CardOrigin.Generated, edited: false, CancellationToken.None));

        await act.Should().ThrowAsync<ArgumentException>(
            "an ownerless card has no account boundary to sit behind");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CountForOwnerAsync_WithNoOwner_IsRefused(string ownerId)
    {
        var act = async () => await WithStoreAsync(store =>
            store.CountForOwnerAsync(ownerId, CancellationToken.None));

        await act.Should().ThrowAsync<ArgumentException>(
            "an ownerless count would be a Cards query with no account boundary");
    }
}
