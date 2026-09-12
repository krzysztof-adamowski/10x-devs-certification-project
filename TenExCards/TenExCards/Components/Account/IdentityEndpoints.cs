using Microsoft.AspNetCore.Identity;
using TenExCards.Data;

namespace Microsoft.AspNetCore.Routing;

/// <summary>
/// A trimmed equivalent of the .NET 10 template's <c>MapAdditionalIdentityEndpoints</c>, carrying
/// only the logout route. The template's external-login and personal-data routes are out of scope
/// — this slice has neither.
/// </summary>
internal static class IdentityEndpoints
{
    public static IEndpointConventionBuilder MapIdentityLogout(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Sign-out is a POST to a real endpoint rather than a component action: clearing the
        // cookie is a response-header operation a circuit cannot perform, and a GET sign-out would
        // be trivially triggerable cross-site.
        return endpoints.MapPost("/Account/Logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return TypedResults.LocalRedirect("~/");
        });
    }
}
