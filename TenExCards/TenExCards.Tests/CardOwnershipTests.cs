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

    // ---- S-04: find, read back, update and delete, all behind the same boundary ----

    private const int AnyLimit = 20;

    private Task<Card> SaveAsync(string ownerId, string prompt, string answer) =>
        WithStoreAsync(store => store.SaveAsync(
            ownerId, prompt, answer, CardOrigin.Generated, false, CancellationToken.None));

    /// <summary>Rewrites CreatedAt directly: the store stamps it, so ordering is otherwise untestable.</summary>
    private async Task SetCreatedAtAsync(Guid id, DateTimeOffset createdAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var card = await db.Cards.SingleAsync(c => c.Id == id);
        card.CreatedAt = createdAt;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task FindForOwnerAsync_WithNoTerm_ReturnsOnlyTheCallersCards()
    {
        var ownerA = await CreateUserAsync();
        var ownerB = await CreateUserAsync();
        await SaveAsync(ownerA, "A's prompt?", "A's answer.");
        await SaveAsync(ownerB, "B's prompt?", "B's answer.");

        var found = await WithStoreAsync(store =>
            store.FindForOwnerAsync(ownerA, null, AnyLimit, CancellationToken.None));

        found.Should().ContainSingle();
        found[0].OwnerId.Should().Be(ownerA);
    }

    [Fact]
    public async Task FindForOwnerAsync_WithTermMatchingAnotherOwnersCard_ReturnsNothing()
    {
        var ownerA = await CreateUserAsync();
        var ownerB = await CreateUserAsync();
        await SaveAsync(ownerB, "Bordeaux mixture?", "A copper fungicide.");

        var found = await WithStoreAsync(store =>
            store.FindForOwnerAsync(ownerA, "Bordeaux", AnyLimit, CancellationToken.None));

        found.Should().BeEmpty("search must not reach across the account boundary");
    }

    [Fact]
    public async Task FindForOwnerAsync_MatchesAnswerTextNotOnlyPrompt()
    {
        var owner = await CreateUserAsync();
        await SaveAsync(owner, "What wraps the key ring?", "An Azure Key Vault key.");

        var found = await WithStoreAsync(store =>
            store.FindForOwnerAsync(owner, "Vault", AnyLimit, CancellationToken.None));

        found.Should().ContainSingle("a learner who remembers the answer must still find the card");
    }

    [Theory]
    [InlineData("VAULT")]
    [InlineData("vault")]
    [InlineData("VaUlT")]
    public async Task FindForOwnerAsync_IsCaseInsensitive(string term)
    {
        var owner = await CreateUserAsync();
        await SaveAsync(owner, "What wraps the key ring?", "An Azure Key Vault key.");

        var found = await WithStoreAsync(store =>
            store.FindForOwnerAsync(owner, term, AnyLimit, CancellationToken.None));

        found.Should().ContainSingle();
    }

    [Fact]
    public async Task FindForOwnerAsync_ReturnsNewestFirstAndRespectsTheLimit()
    {
        var owner = await CreateUserAsync();
        var oldest = await SaveAsync(owner, "Oldest?", "First.");
        var middle = await SaveAsync(owner, "Middle?", "Second.");
        var newest = await SaveAsync(owner, "Newest?", "Third.");

        var baseline = DateTimeOffset.UtcNow;
        await SetCreatedAtAsync(oldest.Id, baseline.AddMinutes(-10));
        await SetCreatedAtAsync(middle.Id, baseline.AddMinutes(-5));
        await SetCreatedAtAsync(newest.Id, baseline);

        var found = await WithStoreAsync(store =>
            store.FindForOwnerAsync(owner, null, 2, CancellationToken.None));

        found.Should().HaveCount(2);
        found[0].Id.Should().Be(newest.Id);
        found[1].Id.Should().Be(middle.Id);
    }

    [Fact]
    public async Task FindForOwnerAsync_WithTiedTimestamps_OrdersDeterministically()
    {
        var owner = await CreateUserAsync();
        var first = await SaveAsync(owner, "Tied one?", "A.");
        var second = await SaveAsync(owner, "Tied two?", "B.");

        // The tie the Id tiebreaker exists for: one batch, one timestamp.
        var tied = DateTimeOffset.UtcNow;
        await SetCreatedAtAsync(first.Id, tied);
        await SetCreatedAtAsync(second.Id, tied);

        var once = await WithStoreAsync(store =>
            store.FindForOwnerAsync(owner, null, AnyLimit, CancellationToken.None));
        var twice = await WithStoreAsync(store =>
            store.FindForOwnerAsync(owner, null, AnyLimit, CancellationToken.None));

        once.Select(c => c.Id).Should().Equal(twice.Select(c => c.Id),
            "an unordered tie at the cutoff hides a card at random between queries");
    }

    [Fact]
    public async Task FindForOwnerAsync_WithLimitBelowOne_StillReturnsSomething()
    {
        var owner = await CreateUserAsync();
        await SaveAsync(owner, "Only card?", "Yes.");

        var found = await WithStoreAsync(store =>
            store.FindForOwnerAsync(owner, null, 0, CancellationToken.None));

        found.Should().NotBeEmpty("a misconfigured cap must not render as an empty collection");
    }

    [Fact]
    public async Task GetForOwnerAsync_ForAnotherOwnersCard_ReturnsNull()
    {
        var ownerA = await CreateUserAsync();
        var ownerB = await CreateUserAsync();
        var bsCard = await SaveAsync(ownerB, "B's prompt?", "B's answer.");

        var seenByA = await WithStoreAsync(store =>
            store.GetForOwnerAsync(ownerA, bsCard.Id, CancellationToken.None));
        var seenByB = await WithStoreAsync(store =>
            store.GetForOwnerAsync(ownerB, bsCard.Id, CancellationToken.None));

        seenByA.Should().BeNull("not yours must be indistinguishable from does not exist");
        seenByB.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateForOwnerAsync_ForAnotherOwnersCard_ReturnsFalseAndChangesNothing()
    {
        var ownerA = await CreateUserAsync();
        var ownerB = await CreateUserAsync();
        var bsCard = await SaveAsync(ownerB, "B's prompt?", "B's answer.");

        var updated = await WithStoreAsync(store => store.UpdateForOwnerAsync(
            ownerA, bsCard.Id, "Hijacked prompt?", "Hijacked answer.", CancellationToken.None));

        updated.Should().BeFalse();
        var stored = await WithStoreAsync(store =>
            store.GetForOwnerAsync(ownerB, bsCard.Id, CancellationToken.None));
        stored!.Prompt.Should().Be("B's prompt?");
        stored.Answer.Should().Be("B's answer.");
    }

    [Fact]
    public async Task DeleteForOwnerAsync_ForAnotherOwnersCard_ReturnsFalseAndLeavesItPresent()
    {
        var ownerA = await CreateUserAsync();
        var ownerB = await CreateUserAsync();
        var bsCard = await SaveAsync(ownerB, "B's prompt?", "B's answer.");

        var deleted = await WithStoreAsync(store =>
            store.DeleteForOwnerAsync(ownerA, bsCard.Id, CancellationToken.None));

        deleted.Should().BeFalse();
        (await WithStoreAsync(store => store.GetForOwnerAsync(ownerB, bsCard.Id, CancellationToken.None)))
            .Should().NotBeNull("one account must not be able to destroy another's card");
    }

    [Fact]
    public async Task UpdateForOwnerAsync_ForOwnCard_ReplacesTextAndPreservesOriginAndEdited()
    {
        var owner = await CreateUserAsync();
        var card = await SaveAsync(owner, "Before?", "Before.");

        var updated = await WithStoreAsync(store => store.UpdateForOwnerAsync(
            owner, card.Id, "After?", "After.", CancellationToken.None));

        updated.Should().BeTrue();
        var stored = await WithStoreAsync(store =>
            store.GetForOwnerAsync(owner, card.Id, CancellationToken.None));
        stored!.Prompt.Should().Be("After?");
        stored.Answer.Should().Be("After.");
        // Both record how the card was CREATED; repairing it later is neither. S-06 reads them.
        stored.Origin.Should().Be(CardOrigin.Generated);
        stored.Edited.Should().BeFalse();
        stored.CreatedAt.Should().Be(card.CreatedAt);
    }

    [Fact]
    public async Task DeleteForOwnerAsync_ForOwnCard_RemovesIt()
    {
        var owner = await CreateUserAsync();
        var card = await SaveAsync(owner, "Doomed?", "Yes.");

        var deleted = await WithStoreAsync(store =>
            store.DeleteForOwnerAsync(owner, card.Id, CancellationToken.None));

        deleted.Should().BeTrue();
        (await WithStoreAsync(store => store.GetForOwnerAsync(owner, card.Id, CancellationToken.None)))
            .Should().BeNull();
        (await WithStoreAsync(store => store.DeleteForOwnerAsync(owner, card.Id, CancellationToken.None)))
            .Should().BeFalse("already gone answers exactly as not yours does");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NewMembers_WithNoOwner_AreRefused(string ownerId)
    {
        var id = Guid.NewGuid();

        var find = async () => await WithStoreAsync(store =>
            store.FindForOwnerAsync(ownerId, null, AnyLimit, CancellationToken.None));
        var get = async () => await WithStoreAsync(store =>
            store.GetForOwnerAsync(ownerId, id, CancellationToken.None));
        var update = async () => await WithStoreAsync(store =>
            store.UpdateForOwnerAsync(ownerId, id, "P", "A", CancellationToken.None));
        var delete = async () => await WithStoreAsync(store =>
            store.DeleteForOwnerAsync(ownerId, id, CancellationToken.None));

        await find.Should().ThrowAsync<ArgumentException>();
        await get.Should().ThrowAsync<ArgumentException>();
        await update.Should().ThrowAsync<ArgumentException>();
        await delete.Should().ThrowAsync<ArgumentException>();
    }
}
