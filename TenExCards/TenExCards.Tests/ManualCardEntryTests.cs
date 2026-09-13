using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenExCards.Cards;
using TenExCards.Data;

namespace TenExCards.Tests;

/// <summary>
/// The manual-entry write path (S-05): the origin it records, the account boundary it sits behind,
/// the length refusal, and the redirect that stops a refresh writing a second card.
/// </summary>
public class ManualCardEntryTests(TenExCardsWebApplicationFactory factory)
    : IClassFixture<TenExCardsWebApplicationFactory>
{
    private const string ValidPassword = "CorrectHorseBattery16";
    private const string Route = "/cards/new";

    private static string UniqueEmail([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $"{caller.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com";

    private HttpClient CreateNoRedirectClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Registers and leaves the client holding that account's auth cookie.</summary>
    private static async Task RegisterAsync(HttpClient client, string email)
    {
        var page = await client.GetStringAsync("/Account/Register");
        var fields = HtmlFormHelpers.ExtractHiddenFields(page, "_handler", "__RequestVerificationToken");
        fields["Input.Email"] = email;
        fields["Input.Password"] = ValidPassword;
        fields["Input.ConfirmPassword"] = ValidPassword;

        using var response = await HtmlFormHelpers.PostFormAsync(client, "/Account/Register", fields);
        response.StatusCode.Should().Be(HttpStatusCode.Found, "registering signs the new account in");
    }

    private static async Task<HttpResponseMessage> PostCardAsync(
        HttpClient client, string prompt, string answer, string url = Route)
    {
        var page = await client.GetStringAsync(url);
        var fields = HtmlFormHelpers.ExtractHiddenFields(page, "_handler", "__RequestVerificationToken");
        fields["Input.Prompt"] = prompt;
        fields["Input.Answer"] = answer;

        return await HtmlFormHelpers.PostFormAsync(client, url, fields);
    }

    private async Task<string> OwnerIdAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        user.Should().NotBeNull();
        return user!.Id;
    }

    private async Task<List<Card>> CardsForAsync(string ownerId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Cards.Where(c => c.OwnerId == ownerId).ToListAsync();
    }

    private async Task<int> CountForAsync(string ownerId)
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICardStore>();
        return await store.CountForOwnerAsync(ownerId, CancellationToken.None);
    }

    [Fact]
    public async Task Post_Authenticated_WritesOneCardWithManualOrigin()
    {
        var email = UniqueEmail();
        using var client = CreateNoRedirectClient();
        await RegisterAsync(client, email);

        using var response = await PostCardAsync(
            client,
            "What does a contained database user authenticate against?",
            "The database named in its own connection string.");

        response.StatusCode.Should().Be(HttpStatusCode.Found);

        var cards = await CardsForAsync(await OwnerIdAsync(email));
        cards.Should().ContainSingle();
        cards[0].Origin.Should().Be(CardOrigin.Manual, "a hand-written card did not come from generation");
        cards[0].Prompt.Should().Be("What does a contained database user authenticate against?");
        cards[0].Answer.Should().Be("The database named in its own connection string.");
    }

    [Fact]
    public async Task Post_Authenticated_CardIsInvisibleToAnotherAccount()
    {
        var mine = UniqueEmail();
        var theirs = UniqueEmail();
        using var myClient = CreateNoRedirectClient();
        using var theirClient = CreateNoRedirectClient();
        await RegisterAsync(myClient, mine);
        await RegisterAsync(theirClient, theirs);

        using var response = await PostCardAsync(myClient, "Mine only", "Not theirs");
        response.StatusCode.Should().Be(HttpStatusCode.Found);

        (await CountForAsync(await OwnerIdAsync(mine))).Should().Be(1);
        (await CountForAsync(await OwnerIdAsync(theirs)))
            .Should().Be(0, "a hand-written card must be invisible to every other account");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Post_OneCharacterOverTheLimit_IsRefusedAndWritesNothing(bool overlongPrompt)
    {
        var email = UniqueEmail();
        using var client = CreateNoRedirectClient();
        await RegisterAsync(client, email);
        var ownerId = await OwnerIdAsync(email);
        var before = await CountForAsync(ownerId);

        var prompt = overlongPrompt ? new string('p', CardBounds.MaxPromptCharacters + 1) : "Short prompt";
        var answer = overlongPrompt ? "Short answer" : new string('a', CardBounds.MaxAnswerCharacters + 1);

        using var response = await PostCardAsync(client, prompt, answer);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a refused form re-renders rather than redirecting");
        (await CountForAsync(ownerId)).Should().Be(before, "an over-length field must write no row at all");
    }

    [Fact]
    public async Task Post_AtExactlyTheLimits_IsAccepted()
    {
        var email = UniqueEmail();
        using var client = CreateNoRedirectClient();
        await RegisterAsync(client, email);

        using var response = await PostCardAsync(
            client,
            new string('p', CardBounds.MaxPromptCharacters),
            new string('a', CardBounds.MaxAnswerCharacters));

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        (await CountForAsync(await OwnerIdAsync(email))).Should().Be(1);
    }

    [Fact]
    public async Task Post_Successful_RedirectsToTheSameRouteAndAFollowingGetWritesNothing()
    {
        var email = UniqueEmail();
        using var client = CreateNoRedirectClient();
        await RegisterAsync(client, email);
        var ownerId = await OwnerIdAsync(email);

        using var response = await PostCardAsync(client, "Redirect prompt", "Redirect answer");

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        var location = response.Headers.Location!.PathAndQuery;
        location.Should().StartWith("/cards/new");
        location.Should().Contain("written=1");
        (await CountForAsync(ownerId)).Should().Be(1);

        // The point of post-redirect-get: refreshing the landing page re-issues this GET, and a
        // GET must never write.
        using var refreshed = await client.GetAsync(location);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CountForAsync(ownerId)).Should().Be(1, "a refresh after a save must not write a second card");
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("99999999")]
    [InlineData("not-a-number")]
    public async Task Get_WithAnAbsurdWrittenValue_StillRendersAndWritesNothing(string written)
    {
        var email = UniqueEmail();
        using var client = CreateNoRedirectClient();
        await RegisterAsync(client, email);
        var ownerId = await OwnerIdAsync(email);

        using var response = await client.GetAsync($"{Route}?written={written}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the tally is cosmetic and must not be able to break the page");
        (await CountForAsync(ownerId)).Should().Be(0, "a GET writes nothing whatever the query string says");
    }
}
