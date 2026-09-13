using AwesomeAssertions;
using Microsoft.Playwright;

namespace TenExCards.E2E;

/// <summary>
/// FR-009, FR-010 and FR-011 from the learner's side: find a saved card, correct it, remove it.
/// The scripted candidates are duplicated as constants because this project deliberately holds no
/// reference to the application — see AGENTS.md.
/// </summary>
[Collection(AppCollection.Name)]
public class SavedCardsTests(AppUnderTest app) : IAsyncLifetime
{
    private const string FirstPrompt = "Scripted candidate one: what is discarded first?";
    private const string SecondPrompt = "Scripted candidate two: what does the learner reword?";
    private const string ThirdPrompt = "Scripted candidate three: what is accepted untouched?";

    // Only the third candidate's answer is distinctive enough to search for on its own.
    private const string ThirdAnswer = "This one is accepted as generated.";

    private IPlaywright _playwright = default!;
    private IBrowser _browser = default!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        var headed = Environment.GetEnvironmentVariable("E2E_HEADED") == "1";
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = !headed });
    }

    public async Task DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
    }

    private static ILocator Action(IPage page, string name) =>
        page.GetByRole(AriaRole.Button, new() { Name = name });

    /// <inheritdoc cref="LearnerJourneyTests"/>
    private static async Task FillOverCircuitAsync(ILocator field, string value, ILocator confirmedBy)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await field.FillAsync(value);

            try
            {
                await Assertions.Expect(confirmedBy).ToBeEnabledAsync(new() { Timeout = 2_000 });
                return;
            }
            catch (PlaywrightException)
            {
            }
        }

        throw new TimeoutException(
            "The circuit never picked up the field; the component still sees it as empty.");
    }

    /// <summary>
    /// The click-side counterpart to <see cref="FillOverCircuitAsync"/>. A click dispatched before
    /// the circuit's WebSocket connects is dropped exactly as an oninput is, and waiting on
    /// prerendered markup does not prove the circuit is live. Re-clicks until the server reacts.
    /// </summary>
    private static async Task ClickOverCircuitAsync(ILocator button, ILocator confirmedBy)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (await confirmedBy.IsVisibleAsync())
            {
                return;
            }

            await button.ClickAsync();

            try
            {
                await Assertions.Expect(confirmedBy).ToBeVisibleAsync(new() { Timeout = 2_000 });
                return;
            }
            catch (PlaywrightException)
            {
            }
        }

        throw new TimeoutException("The circuit never processed the click.");
    }

    private async Task<IPage> RegisterAsync()
    {
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = AppUnderTest.BaseUrl,
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Account/Register");
        await page.GetByLabel("Email").FillAsync($"cards-{Guid.NewGuid():N}@example.com");
        await page.GetByLabel("Password (at least 16 characters)").FillAsync("CorrectHorseBattery16");
        await page.GetByLabel("Confirm password").FillAsync("CorrectHorseBattery16");
        await page.GetByRole(AriaRole.Button, new() { Name = "Register" }).ClickAsync();

        await Action(page, "Sign out").WaitForAsync(new() { Timeout = 20_000 });
        return page;
    }

    /// <summary>Registers, generates, and accepts all three candidates, leaving three saved cards.</summary>
    private async Task<IPage> WithThreeSavedCardsAsync()
    {
        var page = await RegisterAsync();

        await page.GotoAsync("/generate");
        var submit = Action(page, "Generate cards");
        await FillOverCircuitAsync(
            page.GetByLabel("Passage"),
            "A passage the scripted generator ignores, long enough to read as real input.",
            submit);
        await submit.ClickAsync();
        await page.GetByText(FirstPrompt).WaitForAsync(new() { Timeout = 30_000 });

        await Action(page, "Keep this card").ClickAsync();
        await page.GetByText(SecondPrompt).WaitForAsync();
        await Action(page, "Keep this card").ClickAsync();
        await page.GetByText(ThirdPrompt).WaitForAsync();
        await Action(page, "Keep this card").ClickAsync();
        await page.GetByText("Saved 3 cards, discarded 0.").WaitForAsync(new() { Timeout = 20_000 });

        return page;
    }

    private static async Task OpenCardsAsync(IPage page)
    {
        await page.GotoAsync("/cards");
        try
        {
            await page.GetByLabel("Search").WaitForAsync(new() { Timeout = 20_000 });
        }
        catch (TimeoutException)
        {
            var content = await page.ContentAsync();
            throw new TimeoutException(
                $"URL={page.Url} TITLE={await page.TitleAsync()}\n"
                + content[..Math.Min(3000, content.Length)]);
        }
    }

    [Fact]
    public async Task Learner_FindsACardByItsAnswerText_CorrectsIt_AndTheChangeSurvivesAReload()
    {
        var page = await WithThreeSavedCardsAsync();
        await OpenCardsAsync(page);

        // Searching the ANSWER, not the prompt: a learner who remembers only the answer must
        // still reach the card.
        await FillOverCircuitAsync(
            page.GetByLabel("Search"),
            "accepted as generated",
            Action(page, "Edit").First);

        await page.GetByText(ThirdPrompt).WaitForAsync();

        await ClickOverCircuitAsync(Action(page, "Edit").First, page.GetByLabel("Prompt"));
        await page.GetByLabel("Prompt").FillAsync("Corrected prompt for the third card?");
        await Action(page, "Save").ClickAsync();

        await page.GetByText("Card updated.").WaitForAsync(new() { Timeout = 20_000 });

        // The durable half: a reload reads it back out of the store, not out of the circuit.
        await page.ReloadAsync();
        await page.GetByText("Corrected prompt for the third card?")
            .WaitForAsync(new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task Learner_EditingPastTheColumnBound_IsRefusedInPlaceAndStaysInEditState()
    {
        var page = await WithThreeSavedCardsAsync();
        await OpenCardsAsync(page);

        await ClickOverCircuitAsync(Action(page, "Edit").First, page.GetByLabel("Prompt"));
        await page.GetByLabel("Prompt").FillAsync(new string('x', 501));
        await Action(page, "Save").ClickAsync();

        await page.GetByText("The limit is").WaitForAsync(new() { Timeout = 20_000 });

        // Still in edit state: the refusal must not throw the learner's typing away.
        await Assertions.Expect(Action(page, "Save")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByLabel("Prompt")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Learner_Deleting_NeedsTheSecondClick_AndCancelLeavesTheCardAlone()
    {
        var page = await WithThreeSavedCardsAsync();
        await OpenCardsAsync(page);

        await page.GetByText(ThirdPrompt).WaitForAsync();

        // First click only arms it.
        await ClickOverCircuitAsync(Action(page, "Delete").First, Action(page, "Delete permanently"));
        await Assertions.Expect(Action(page, "Delete permanently")).ToBeVisibleAsync();
        await page.GetByText(ThirdPrompt).WaitForAsync();

        // Cancel disarms it, card untouched.
        await Action(page, "Cancel").ClickAsync();
        await Assertions.Expect(Action(page, "Delete permanently")).ToBeHiddenAsync();
        await page.GetByText(ThirdPrompt).WaitForAsync();

        // Second click commits, and the card is gone after a reload.
        await ClickOverCircuitAsync(Action(page, "Delete").First, Action(page, "Delete permanently"));
        await Action(page, "Delete permanently").ClickAsync();
        await page.GetByText("Card deleted.").WaitForAsync(new() { Timeout = 20_000 });

        await page.ReloadAsync();
        await page.GetByLabel("Search").WaitForAsync(new() { Timeout = 20_000 });
        (await page.GetByText(ThirdPrompt).CountAsync())
            .Should().Be(0, "a deleted card must not come back on a reload");
    }

    [Fact]
    public async Task Learner_OpeningEditOnOneRow_ClosesAnArmedDeleteOnAnother()
    {
        var page = await WithThreeSavedCardsAsync();
        await OpenCardsAsync(page);

        await page.GetByText(ThirdPrompt).WaitForAsync();

        await ClickOverCircuitAsync(Action(page, "Delete").First, Action(page, "Delete permanently"));

        // Editing a different row must disarm it — at most one row is ever out of view state.
        await ClickOverCircuitAsync(Action(page, "Edit").Last, page.GetByLabel("Prompt"));

        await Assertions.Expect(Action(page, "Delete permanently")).ToBeHiddenAsync();
        await Assertions.Expect(page.GetByLabel("Prompt")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TriageSummary_LinksToTheSavedCards()
    {
        // S-02 deliberately left this link out because the surface did not exist yet.
        var page = await WithThreeSavedCardsAsync();

        // Scoped to the content area: the nav carries a link of the same name, which is correct.
        var link = page.GetByRole(AriaRole.Article)
            .GetByRole(AriaRole.Link, new() { Name = "Your cards" });
        await Assertions.Expect(link).ToBeVisibleAsync();

        await link.ClickAsync();
        await page.GetByLabel("Search").WaitForAsync(new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task Learner_WithNoSavedCards_IsToldSoAndPointedAtGenerate()
    {
        var page = await RegisterAsync();
        await OpenCardsAsync(page);

        await page.GetByText("You have no saved cards yet").WaitForAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Generate some from a passage" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task Learner_SearchingForSomethingAbsent_IsToldDistinctlyFromOwningNothing()
    {
        var page = await WithThreeSavedCardsAsync();
        await OpenCardsAsync(page);

        // Prove the circuit is live on a term that matches BEFORE typing one that does not: an
        // oninput dispatched before the WebSocket connects is lost, and the empty result would
        // then be indistinguishable from a search that never reached the server.
        await FillOverCircuitAsync(
            page.GetByLabel("Search"), "accepted as generated", Action(page, "Edit").First);

        await page.GetByLabel("Search").FillAsync("nothingmatchesthisterm");

        await page.GetByText("Nothing matches").WaitForAsync(new() { Timeout = 20_000 });

        // Distinct from the no-cards-at-all state, which would mislead a learner who has cards.
        (await page.GetByText("You have no saved cards yet").CountAsync())
            .Should().Be(0, "owning nothing and matching nothing are different situations");
    }
}
