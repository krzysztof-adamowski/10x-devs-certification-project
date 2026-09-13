using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
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
/// Replaces the hand-run grep behind the ".AllowAnonymous()" count rule; see
/// TenExCards/AGENTS.md.
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

    // Gated routes this repository owns. The framework's own endpoints are asserted
    // separately, by prefix — see FrameworkEndpoints_AreGated.
    private static readonly string[] ExpectedGated =
    [
        "/cards",
        "/cards/new",
        "/generate",
        "{**path:file}",
    ];

    // Emitted by AddInteractiveServerRenderMode(), not by anything here, and the SDK floats
    // on 10.0.x — so pinning their names would let a runtime patch block a deploy for a
    // reason unrelated to this repository. The invariant is that they are gated, not what
    // they are called.
    private static readonly string[] FrameworkPrefixes = ["/_blazor", "/_framework/"];

    private static bool IsStaticAsset(Endpoint endpoint) =>
        endpoint.Metadata.Any(m => m.GetType().FullName?.Contains("StaticAsset") == true);

    private static bool IsFrameworkOwned(Endpoint endpoint) =>
        endpoint is RouteEndpoint route
        && FrameworkPrefixes.Any(prefix =>
            ("/" + (route.RoutePattern.RawText ?? string.Empty).TrimStart('/')).StartsWith(prefix));

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
        var routable = Endpoints()
            .Where(e => !IsStaticAsset(e) && !IsFrameworkOwned(e))
            .ToList();

        var anonymous = routable
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Pattern).Distinct().Order(StringComparer.Ordinal).ToList();
        var gated = routable
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Select(Pattern).Distinct().Order(StringComparer.Ordinal).ToList();

        anonymous.Should().Equal(ExpectedAnonymous.Order(StringComparer.Ordinal),
            "a route becoming anonymous is invisible to a status-code observer; add it here "
            + "deliberately or remove the marker that put it there");
        gated.Should().Equal(ExpectedGated.Order(StringComparer.Ordinal),
            "a gated route losing its gate returns 200 instead of 302 and nothing else reports it");
    }

    [Fact]
    public void FrameworkEndpoints_AreGated()
    {
        // MapRazorComponents<App>() returns one builder covering every page route, so a single
        // .AllowAnonymous() on it would move these AND every page across at once.
        // Static assets live under /_framework/ too and are anonymous by design.
        var framework = Endpoints()
            .Where(e => !IsStaticAsset(e) && IsFrameworkOwned(e))
            .ToList();

        framework.Should().NotBeEmpty("the render-mode builder must still map its endpoints");
        framework.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Pattern)
            .Should().BeEmpty(
                "the render-mode builder is deliberately left un-anonymous; marking it would "
                + "exempt every future gated page, not just the circuit hub");
    }

    [Fact]
    public async Task EnhancedNavigation_ToGatedRoute_StillRedirectsToLogin()
    {
        // Routes.razor carries no @rendermode, so every navigation is an HTTP request the
        // fallback policy sees — which is why Components/Account/RedirectToLogin.razor has no
        // trigger. Goes red the day Routes.razor becomes interactive and it does.
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/generate");
        request.Headers.Add("blazor-enhanced-nav", "on");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        // Absolute here, as AuthBoundaryTests records for TestServer's cookie challenge.
        response.Headers.Location!.PathAndQuery.Should().StartWith("/Account/Login");
    }
}
