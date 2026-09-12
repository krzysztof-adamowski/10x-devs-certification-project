using Microsoft.AspNetCore.Identity;

namespace TenExCards.Data;

/// <summary>
/// The application's own user type, introduced now so <c>S-02</c>'s card-to-owner relationship and
/// <c>S-06</c>'s per-account outcome recording are additive rather than a type change rippling
/// through the context base, DI and every component.
/// </summary>
/// <remarks>
/// Empty on purpose. Adding a member here is expected future work, not a reason to delete this
/// class as speculative generality before that work arrives.
/// </remarks>
public class ApplicationUser : IdentityUser
{
}
