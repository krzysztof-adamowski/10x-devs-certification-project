using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace TenExCards.Tests;

/// <summary>
/// Pins which endpoints carry <see cref="IAllowAnonymous"/>, because the fragile surface is
/// metadata rather than ordering: an ordering mistake is loud (login loop, 400 on every form),
/// while an attribute appearing in a file that never mentions authorization is silent and
/// produces the same status code either way.
/// </summary>
/// <remarks>
/// Replaces the hand-run grep that guarded the ".AllowAnonymous() appears on exactly the known
/// surfaces" rule in TenExCards/AGENTS.md. A new page failing this test is the test working —
/// classify the page, do not relax the list.
/// </remarks>
public class PipelineMetadataTests(TenExCardsWebApplicationFactory factory)
    : IClassFixture<TenExCardsWebApplicationFactory>
{
    // Reachable without signing in. Four sources: Home, Error, NotFound, and the Identity pages,
    // which inherit [AllowAnonymous] from Components/Account/Pages/_Imports.razor — the folder
    // inheritance that adds a page to this set without anyone writing an attribute. Logout is the
    // POST endpoint from IdentityEndpoints.cs, anonymous so a repeat post does not 302 to login.
    private static readonly string[] ExpectedAnonymous =
    [
        "/",
        "/Account/Login",
        "/Account/Logout",
        "/Account/Register",
        "/Error",
        "/not-found",
    ];

    // The three gated pages, the unmatched-route fallback, and the render-mode builder's own
    // endpoints. THAT LAST GROUP IS THE POINT: MapRazorComponents<App>() returns one builder
    // covering every page route, so a single .AllowAnonymous() on it would move every row above
    // the line at once. They are gated because nobody marked it — an absence, asserted here.
    private static readonly string[] ExpectedGated =
    [
        "/_blazor",
        "/_blazor/disconnect/",
        "/_blazor/initializers/",
        "/_blazor/negotiate",
        "/_framework/opaque-redirect",
        "/cards",
        "/cards/new",
        "/generate",
        "{**path:file}",
    ];

    private static bool IsStaticAsset(Endpoint endpoint) =>
        endpoint.Metadata.Any(m => m.GetType().FullName?.Contains("StaticAsset") == true);

    private static string Pattern(Endpoint endpoint) =>
        (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? endpoint.DisplayName ?? "(unnamed)";

    private IReadOnlyList<Endpoint> Endpoints()
    {
        // The graph is not built until a client exists.
        using var _ = factory.CreateClient();
        return factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .ToList();
    }

    [Fact]
    public void StaticAssets_AreAnonymous()
    {
        var assets = Endpoints().Where(IsStaticAsset).ToList();

        // The control, in the same assertion: a broken predicate returns an empty set, and
        // "every element of nothing is anonymous" would pass while proving nothing. See
        // lessons.md, "A negative check needs a control, or it cannot fail".
        assets.Should().NotBeEmpty(
            "the StaticAsset metadata predicate must still match, or this check cannot fail");

        assets.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Select(Pattern)
            .Should().BeEmpty(
                "MapStaticAssets() is .AllowAnonymous(); without it every stylesheet and script "
                + "302s to the login path and the sign-in page is served as the stylesheet");
    }

    [Fact]
    public void RoutableEndpoints_MatchTheRecordedAuthorizationSplit()
    {
        var routable = Endpoints().Where(e => !IsStaticAsset(e)).ToList();

        var anonymous = routable
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Pattern).Distinct().Order().ToList();
        var gated = routable
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Select(Pattern).Distinct().Order().ToList();

        anonymous.Should().Equal(ExpectedAnonymous.Order(),
            "a route becoming anonymous is invisible to a status-code observer; add it here "
            + "deliberately or remove the marker that put it there");
        gated.Should().Equal(ExpectedGated.Order(),
            "a gated route losing its gate returns 200 instead of 302 and nothing else reports it");
    }

    [Fact]
    public async Task EnhancedNavigation_ToGatedRoute_StillRedirectsToLogin()
    {
        // Routes.razor carries no @rendermode, so the Router is statically rendered and every
        // navigation is an HTTP request the fallback policy sees. That is why the
        // <NotAuthorized><RedirectToLogin /></NotAuthorized> fragment has no trigger today.
        // This goes red the day Routes.razor becomes interactive — the change that would make
        // that fragment load-bearing instead of dead.
        using var client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/generate");
        request.Headers.Add("blazor-enhanced-nav", "on");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Found);
        // Absolute here, as AuthBoundaryTests records for TestServer's cookie challenge.
        response.Headers.Location!.PathAndQuery.Should().StartWith("/Account/Login");
    }
}
