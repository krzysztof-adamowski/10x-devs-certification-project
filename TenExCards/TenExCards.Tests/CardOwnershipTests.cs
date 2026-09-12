using AwesomeAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenExCards.Cards;
using TenExCards.Data;

namespace TenExCards.Tests;

/// <summary>
/// The account boundary around the product's first entity. <c>TenExCards.Tests/AGENTS.md</c> calls
/// this "the invariant this project exists to protect" and requires every persisting slice from
/// <c>S-02</c> onward to assert its own queries sit behind it — this file is S-02's share.
/// </summary>
/// <remarks>
/// These run against the factory's in-memory database, which does NOT enforce foreign keys or
/// <c>HasMaxLength</c>. Real users are still created through <see cref="UserManager{TUser}"/> so the
/// owner ids are the same shape the application will actually pass; the assertions below are about
/// <see cref="ICardStore"/>'s filtering, not about the provider's constraints.
/// </remarks>
public class CardOwnershipTests(TenExCardsWebApplicationFactory factory)
    : IClassFixture<TenExCardsWebApplicationFactory>
{
    private const string ValidPassword = "CorrectHorseBattery16";

    private async Task<string> CreateUserAsync()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = $"owner-{Guid.NewGuid():N}@example.com",
            Email = $"owner-{Guid.NewGuid():N}@example.com",
        };
        var result = await userManager.CreateAsync(user, ValidPassword);
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Description)));
        return user.Id;
    }

    /// <summary>
    /// A fresh scope per call, because <see cref="ICardStore"/> is registered scoped alongside the
    /// scoped <see cref="AppDbContext"/> — sharing one scope across a whole test would let EF's
    /// change tracker answer a read that the database never saw.
    /// </summary>
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
            "The database named in its own connection string.", CardOrigin.Generated, CancellationToken.None));

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
                ownerA, $"Prompt A{i}", $"Answer A{i}", CardOrigin.Generated, CancellationToken.None));
        }

        await WithStoreAsync(store => store.SaveAsync(
            ownerB, "Prompt B0", "Answer B0", CardOrigin.Generated, CancellationToken.None));

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
            "Complete.", CardOrigin.Generated, CancellationToken.None));

        saved.OwnerId.Should().Be(ownerA);
        saved.Origin.Should().Be(CardOrigin.Generated);
        saved.CreatedAt.Should().NotBe(default);
        saved.CreatedAt.Should().BeOnOrAfter(before);

        // Read it back through a different scope's context, so the assertion is about what was
        // written rather than about the change tracker that wrote it.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Cards.SingleAsync(c => c.Id == saved.Id, CancellationToken.None);

        stored.OwnerId.Should().Be(ownerA);
        stored.Origin.Should().Be(CardOrigin.Generated);
        stored.Prompt.Should().Be(saved.Prompt);
        stored.Answer.Should().Be(saved.Answer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveAsync_WithNoOwner_IsRefused(string ownerId)
    {
        var act = async () => await WithStoreAsync(store => store.SaveAsync(
            ownerId, "Prompt", "Answer", CardOrigin.Generated, CancellationToken.None));

        await act.Should().ThrowAsync<ArgumentException>(
            "an ownerless card has no account boundary to sit behind");
    }
}
