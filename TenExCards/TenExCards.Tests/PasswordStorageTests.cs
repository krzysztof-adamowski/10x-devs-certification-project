using AwesomeAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TenExCards.Data;

namespace TenExCards.Tests;

/// <summary>
/// Asserts password storage, which this slice inherits from <c>AddIdentityCore</c> rather than
/// writing — the one security-relevant setting nobody in this codebase actually configured, and
/// therefore the one most likely to silently drift.
/// </summary>
public class PasswordStorageTests(TenExCardsWebApplicationFactory factory)
    : IClassFixture<TenExCardsWebApplicationFactory>
{
    [Fact]
    public void PasswordHasher_Resolved_IsFrameworkDefaultWithIdentityV3Compatibility()
    {
        using var scope = factory.Services.CreateScope();

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();
        hasher.Should().BeOfType<PasswordHasher<ApplicationUser>>();

        var options = scope.ServiceProvider.GetRequiredService<IOptions<PasswordHasherOptions>>().Value;
        options.CompatibilityMode.Should().Be(PasswordHasherCompatibilityMode.IdentityV3);

        // Pinned to the value observed at implementation time (.NET 10, 10.0.12) by running this
        // assertion and reading back the actual number from the failure message, not guessed.
        // The default moved once already (10,000 -> 100,000 in .NET 8); the point of asserting
        // it is to make the NEXT change visible as a failing test naming old vs. new, rather
        // than passing silently.
        options.IterationCount.Should().Be(100000);
    }

    [Fact]
    public async Task PasswordHash_AfterUserCreated_IsNeitherNullNorPlaintext()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"hash-check-{Guid.NewGuid():N}@example.com";
        const string password = "SomeValidPassword1234567";

        var user = new ApplicationUser { UserName = email, Email = email };
        var result = await userManager.CreateAsync(user, password);

        result.Succeeded.Should().BeTrue(string.Join(", ", result.Errors.Select(e => e.Description)));
        user.PasswordHash.Should().NotBeNullOrEmpty();
        user.PasswordHash.Should().NotBe(password);
    }
}
