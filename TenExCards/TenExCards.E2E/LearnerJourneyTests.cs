using AwesomeAssertions;
using Microsoft.Playwright;

namespace TenExCards.E2E;

/// <summary>
/// US-01 from the learner's side, which is the form the requirement is stated in. Selection is by
/// accessible name and label rather than CSS class, so restyling does not break the suite and the
/// assertions double as an accessibility check.
/// </summary>
public class LearnerJourneyTests(AppUnderTest app) : IClassFixture<AppUnderTest>, IAsyncLifetime
{
    private const string FirstPrompt = "Scripted candidate one: what is discarded first?";
    private const string SecondPrompt = "Scripted candidate two: what does the learner reword?";
    private const string ThirdPrompt = "Scripted candidate three: what is accepted untouched?";

    private const string Keep = "Keep this card";
    private const string Discard = "Discard this card";
    private const string EditWording = "Edit wording";

    private IPlaywright _playwright = default!;
    private IBrowser _browser = default!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();

        // PWDEBUG=1 or E2E_HEADED=1 to watch it run.
        var headed = Environment.GetEnvironmentVariable("E2E_HEADED") == "1";
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = !headed });
    }

    public async Task DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
    }

    private async Task<IPage> RegisterAndSignInAsync()
    {
        var context = await _browser.NewContextAsync(new() { BaseURL = AppUnderTest.BaseUrl });
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Account/Register");
        await page.GetByLabel("Email").FillAsync($"learner-{Guid.NewGuid():N}@example.com");
        await page.GetByLabel("Password (at least 16 characters)").FillAsync("CorrectHorseBattery16");
        await page.GetByLabel("Confirm password").FillAsync("CorrectHorseBattery16");
        await page.GetByRole(AriaRole.Button, new() { Name = "Register" }).ClickAsync();

        // Registration signs the learner in; the sign-out control is the proof.
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign out" })
            .WaitForAsync(new() { Timeout = 20_000 });

        return page;
    }

    /// <summary>
    /// Fills a bound field and confirms the server actually saw it. An oninput dispatched before
    /// the circuit's WebSocket connects is simply lost — the DOM holds the text while the component
    /// still believes the field is empty — so a single Fill races the connection and the submit
    /// button never enables. Re-filling is what closes that race.
    /// </summary>
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

    private static async Task GenerateAsync(IPage page)
    {
        await page.GotoAsync("/generate");

        var submit = page.GetByRole(AriaRole.Button, new() { Name = "Generate cards" });
        await FillOverCircuitAsync(
            page.GetByLabel("Passage"),
            "A passage the scripted generator ignores, long enough to read as real input.",
            submit);

        await submit.ClickAsync();
        await page.GetByText(FirstPrompt).WaitForAsync(new() { Timeout = 30_000 });
    }

    private static ILocator Action(IPage page, string name) =>
        page.GetByRole(AriaRole.Button, new() { Name = name });

    [Fact]
    public async Task Learner_RegistersPastesTriagesAndReadsTheSummary()
    {
        var page = await RegisterAndSignInAsync();
        await GenerateAsync(page);

        // "Equal prominence" made machine-checkable: same row, same width, no confirmation step.
        await AssertEquallyProminentAsync(
            Action(page, Keep), Action(page, Discard), Action(page, EditWording));

        // One: discarded.
        await Action(page, Discard).ClickAsync();
        await page.GetByText(SecondPrompt).WaitForAsync();

        // Two: edited, then accepted.
        await Action(page, EditWording).ClickAsync();
        await page.GetByLabel("Prompt").FillAsync(SecondPrompt + " (reworded by the learner)");
        await Action(page, Keep).ClickAsync();
        await page.GetByText(ThirdPrompt).WaitForAsync();

        // Three: accepted untouched.
        await Action(page, Keep).ClickAsync();

        // TEMPORARY deliberate break — S-03 criterion 3.8, reverted in the next commit.
        await page.GetByText("Saved 99 cards, discarded 99.").WaitForAsync(new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task Learner_CancellingAnEdit_RestoresTheWordingAndEveryOriginalAction()
    {
        var page = await RegisterAndSignInAsync();
        await GenerateAsync(page);

        await Action(page, EditWording).ClickAsync();
        await page.GetByLabel("Prompt").FillAsync("Typing that is about to be abandoned");
        await Action(page, "Cancel").ClickAsync();

        await page.GetByText(FirstPrompt).WaitForAsync();

        foreach (var name in new[] { Keep, Discard, EditWording })
        {
            (await Action(page, name).IsVisibleAsync())
                .Should().BeTrue($"{name} must come back after Cancel");
        }
    }

    [Fact]
    public async Task Learner_EmptyingAField_CannotCommitButCanStillCancel()
    {
        var page = await RegisterAndSignInAsync();
        await GenerateAsync(page);

        await Action(page, EditWording).ClickAsync();
        await page.GetByLabel("Answer").FillAsync("");

        await Assertions.Expect(Action(page, Keep)).ToBeDisabledAsync();

        // Cancel stays available: an invalid edit must never trap the learner on the card.
        await Assertions.Expect(Action(page, "Cancel")).ToBeEnabledAsync();
    }

    [Fact]
    public async Task Learner_EditingPastTheColumnBound_CannotCommitAndSeesTheCounterRedden()
    {
        var page = await RegisterAndSignInAsync();
        await GenerateAsync(page);

        await Action(page, EditWording).ClickAsync();
        await page.GetByLabel("Prompt").FillAsync(new string('x', 501));

        await Assertions.Expect(Action(page, Keep)).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("div.form-text.text-danger")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Learner_PastingPastThePassageLimit_IsRefusedBeforeGeneratingBegins()
    {
        var page = await RegisterAndSignInAsync();

        await page.GotoAsync("/generate");

        // Proves the circuit is live before the over-long paste, so a disabled button below means
        // the bound length, not a lost input event.
        var submit = page.GetByRole(AriaRole.Button, new() { Name = "Generate cards" });
        await FillOverCircuitAsync(page.GetByLabel("Passage"), "A short, valid passage.", submit);

        await page.GetByLabel("Passage").FillAsync(new string('x', 12_001));

        await Assertions.Expect(submit).ToBeDisabledAsync();

        // The counter names the limit, and the generating state is never entered — so the refusal
        // costs no quota and no thirty-second wait. The separator is whatever the server's culture
        // uses, so the regex tolerates a comma, a space or none rather than pinning en-US.
        await Assertions.Expect(page.Locator("div.form-text.text-danger").First)
            .ToContainTextAsync(new System.Text.RegularExpressions.Regex(@"12\D?000 characters"));
        await Assertions.Expect(page.GetByText("Reading your passage and writing cards."))
            .Not.ToBeVisibleAsync();
    }

    private static async Task AssertEquallyProminentAsync(params ILocator[] actions)
    {
        var widths = new List<float>();

        foreach (var action in actions)
        {
            (await action.IsVisibleAsync()).Should().BeTrue();
            var box = await action.BoundingBoxAsync();
            box.Should().NotBeNull();
            widths.Add(box!.Width);
        }

        widths.Distinct().Should().ContainSingle(
            "US-01 requires accept, reject and edit at equal prominence, achieved by literal sameness");
    }
}
