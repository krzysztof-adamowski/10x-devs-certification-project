using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TenExCards.Cards;
using TenExCards.Data;

namespace TenExCards.Tests;

/// <summary>
/// The /cards page's prerendered output. InteractiveServer cannot be driven by this harness, but
/// the first render is ordinary HTML — which is enough to pin the states that depend on how many
/// cards the learner owns, and those are awkward to stage in the browser suite.
/// </summary>
public class SavedCardsPageTests(TenExCardsWebApplicationFactory factory)
    : IClassFixture<TenExCardsWebApplicationFactory>
{
    private const string ValidPassword = "CorrectHorseBattery16";

    private async Task<HttpClient> SignedInClientAsync(int cardsToSeed)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var email = $"page-{Guid.NewGuid():N}@example.com";
        var page = await client.GetStringAsync("/Account/Register");
        var fields = HtmlFormHelpers.ExtractHiddenFields(page, "_handler", "__RequestVerificationToken");
        fields["Input.Email"] = email;
        fields["Input.Password"] = ValidPassword;
        fields["Input.ConfirmPassword"] = ValidPassword;
        var registered = await HtmlFormHelpers.PostFormAsync(client, "/Account/Register", fields);
        registered.StatusCode.Should().Be(HttpStatusCode.Found);

        if (cardsToSeed > 0)
        {
            using var scope = factory.Services.CreateScope();
            var users = scope.ServiceProvider
                .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(email);
            var store = scope.ServiceProvider.GetRequiredService<ICardStore>();

            for (var i = 0; i < cardsToSeed; i++)
            {
                await store.SaveAsync(
                    user!.Id, $"Seeded prompt {i}?", $"Seeded answer {i}.",
                    CardOrigin.Generated, false, CancellationToken.None);
            }
        }

        return client;
    }

    /// <summary>
    /// The &lt;article&gt; the layout renders the page into. NavMenu sits outside it and links to
    /// both "cards" and "generate" on every page, so a whole-document assertion proves nothing
    /// about the page under test.
    /// </summary>
    private static string Content(string html)
    {
        var start = html.IndexOf("<article", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the layout renders an <article> element");
        var end = html.IndexOf("</article>", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(-1, "the <article> element is closed");
        return html[start..end];
    }

    private int MaxResults
    {
        get
        {
            using var scope = factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IOptions<CardOptions>>().Value.MaxResults;
        }
    }

    [Fact]
    public async Task CardsPage_WithNoCards_SaysSoAndPointsAtGenerate()
    {
        using var client = await SignedInClientAsync(cardsToSeed: 0);

        var html = await client.GetStringAsync("/cards");

        html.Should().Contain("You have no saved cards yet");
        Content(html).Should().Contain("href=\"generate\"", "the empty state points at generation");
    }

    [Fact]
    public async Task CardsPage_AtTheCap_SaysTheListIsCappedAndSearchReachesTheRest()
    {
        // One more than the cap, so the list is genuinely truncated rather than merely equal to it.
        using var client = await SignedInClientAsync(cardsToSeed: MaxResults + 1);

        var html = await client.GetStringAsync("/cards");

        // The honesty requirement: a learner with more cards than fit must not read this as
        // their whole collection.
        html.Should().Contain("most recent cards");
        html.Should().Contain("Search to find others");
        html.Should().NotContain("You have no saved cards yet");
    }

    [Fact]
    public async Task Home_WhenSignedIn_LinksToTheSavedCards()
    {
        using var client = await SignedInClientAsync(cardsToSeed: 0);

        var html = await client.GetStringAsync("/");

        // Scoped to <article>: NavMenu renders href="cards" on EVERY page, so an unscoped
        // assertion survives deleting the Home link it claims to test.
        Content(html).Should().Contain("href=\"cards\"", "a signed-in learner reaches their cards from home");
    }

    [Fact]
    public async Task CardsPage_BelowTheCap_DoesNotClaimToBeCapped()
    {
        using var client = await SignedInClientAsync(cardsToSeed: 2);

        var html = await client.GetStringAsync("/cards");

        html.Should().Contain("Seeded prompt 0?");
        html.Should().NotContain("Search to find others");
    }
}
