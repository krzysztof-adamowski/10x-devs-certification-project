using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TenExCards.Data;

namespace TenExCards.Tests;

/// <summary>
/// The invariant that stops S-02 leaking one learner's cards to another: anonymous access to the
/// allowlisted routes, a 302 (never 401) challenge for everything else, and the register/sign-in/
/// sign-out round trip that proves the boundary actually moves.
/// </summary>
public class AuthBoundaryTests(TenExCardsWebApplicationFactory factory)
    : IClassFixture<TenExCardsWebApplicationFactory>
{
    private const string ValidPassword = "CorrectHorseBattery16";

    private static string UniqueEmail([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $"{caller.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com";

    private HttpClient CreateNoRedirectClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> RegisterAsync(
        HttpClient client, string email, string password)
    {
        var page = await client.GetStringAsync("/Account/Register");
        var fields = HtmlFormHelpers.ExtractHiddenFields(page, "_handler", "__RequestVerificationToken");
        fields["Input.Email"] = email;
        fields["Input.Password"] = password;
        fields["Input.ConfirmPassword"] = password;
        return await HtmlFormHelpers.PostFormAsync(client, "/Account/Register", fields);
    }

    private async Task<ApplicationUser?> FindUserByEmailAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await userManager.FindByEmailAsync(email);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Error")]
    [InlineData("/not-found")]
    public async Task AnonymousRoute_NoSession_ReturnsOk(string path)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GatedRoute_Unauthenticated_RedirectsToLogin()
    {
        using var client = CreateNoRedirectClient();

        var response = await client.GetAsync("/circuit-check");

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        // TestServer's cookie challenge reports Location as an absolute http://localhost/... URI
        // even though the real header text is relative; compare the path, not the raw string.
        response.Headers.Location!.PathAndQuery.Should().StartWith("/Account/Login");
    }

    [Fact]
    public async Task AccountLifecycle_RegisterThenSignOut_GatedAccessFollowsSessionState()
    {
        var email = UniqueEmail();
        using var client = CreateNoRedirectClient();

        var registerResponse = await RegisterAsync(client, email, ValidPassword);
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Found, "a successful registration redirects home");

        // Land signed in.
        var homeResponse = await client.GetAsync("/");
        var homeHtml = await homeResponse.Content.ReadAsStringAsync();
        homeHtml.Should().Contain($"Signed in as <strong>{email}</strong>");

        // Reach a gated route.
        var gatedWhileSignedIn = await client.GetAsync("/circuit-check");
        gatedWhileSignedIn.StatusCode.Should().Be(HttpStatusCode.OK);

        // Sign out.
        var antiforgeryField = HtmlFormHelpers.ExtractHiddenFields(homeHtml, "__RequestVerificationToken");
        var logoutResponse = await HtmlFormHelpers.PostFormAsync(client, "/Account/Logout", antiforgeryField);
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.Found);

        // Lose access to the gated route.
        var gatedAfterSignOut = await client.GetAsync("/circuit-check");
        gatedAfterSignOut.StatusCode.Should().Be(HttpStatusCode.Found);
        gatedAfterSignOut.Headers.Location!.PathAndQuery.Should().StartWith("/Account/Login");
    }

    [Fact]
    public async Task Registration_OnSuccess_SetsUserNameToSubmittedEmail()
    {
        var email = UniqueEmail();
        using var client = CreateNoRedirectClient();

        await RegisterAsync(client, email, ValidPassword);
        var user = await FindUserByEmailAsync(email);

        // The one assertion protecting the invariant this plan flags repeatedly: EmailIndex is
        // non-unique, so the unique UserNameIndex behind UserName is the only database-level
        // guard against two concurrent registrations for the same address.
        user.Should().NotBeNull();
        user!.UserName.Should().Be(email);
    }

    [Fact]
    public async Task Registration_DuplicateEmail_IsRefusedWithReason()
    {
        var email = UniqueEmail();
        using var client = CreateNoRedirectClient();

        await RegisterAsync(client, email, ValidPassword);
        var secondResponse = await RegisterAsync(client, email, ValidPassword);

        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK, "a refusal re-renders the form, it does not redirect");
        var html = await secondResponse.Content.ReadAsStringAsync();
        html.Should().Contain("already taken");
    }

    [Fact]
    public async Task Registration_PasswordTooShort_RefusedBeforeAccountCreated()
    {
        var email = UniqueEmail();
        using var client = CreateNoRedirectClient();

        var response = await RegisterAsync(client, email, "short1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("at least 16 characters");

        (await FindUserByEmailAsync(email)).Should().BeNull("the refusal must happen before an account exists");
    }
}
