using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace TenExCards.Tests;

/// <summary>
/// Asserts the policy this project configured deliberately — not Identity's defaults — so a
/// later agent restoring the defaults "as a fix" fails a test naming exactly what changed.
/// </summary>
public class IdentityConfigurationTests(TenExCardsWebApplicationFactory factory)
    : IClassFixture<TenExCardsWebApplicationFactory>
{
    [Fact]
    public void WebApplicationFactory_NoEnvironmentOverride_DefaultsToDevelopment()
    {
        // The plan calls for verifying this rather than assuming it, because
        // TenExCardsWebApplicationFactory's explicit UseEnvironment(Development) is either
        // belt-and-braces or the only thing standing between a unit test and a Key Vault call —
        // depending on which way this goes. Measured: it goes the belt-and-braces way.
        // WebApplicationFactory sets ASPNETCORE_ENVIRONMENT to Development on its own, so
        // Program.cs's `if (!builder.Environment.IsDevelopment())` Key Vault guard already
        // skips without the explicit override — kept anyway so this fact staying true is
        // asserted, not assumed, and so the factory does not silently start reaching Key Vault
        // if a future .NET version changes that default.
        using var bareFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=unused;Database=unused;");
            builder.UseSetting("Testing:SkipStartupMigration", "true");
        });

        var environment = bareFactory.Services.GetRequiredService<IHostEnvironment>();

        environment.EnvironmentName.Should().Be(Environments.Development);
    }

    [Fact]
    public void PasswordPolicy_Configured_FavoursLengthOverComposition()
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;

        options.Password.RequiredLength.Should().Be(16);
        options.Password.RequireDigit.Should().BeFalse();
        options.Password.RequireLowercase.Should().BeFalse();
        options.Password.RequireUppercase.Should().BeFalse();
        options.Password.RequireNonAlphanumeric.Should().BeFalse();
    }

    [Fact]
    public void Lockout_NewUser_IsEnabled()
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;

        options.Lockout.AllowedForNewUsers.Should().BeTrue();
    }

    [Fact]
    public void ApplicationCookie_Configured_UsesSevenDaySlidingExpiration()
    {
        using var scope = factory.Services.CreateScope();
        var cookieOptions = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        cookieOptions.ExpireTimeSpan.Should().Be(TimeSpan.FromDays(7));
        cookieOptions.SlidingExpiration.Should().BeTrue();
        cookieOptions.LoginPath.Value.Should().Be("/Account/Login");
    }

    [Fact]
    public void IdentityOptions_Configured_AllowsUnconfirmedAccountButRequiresUniqueEmail()
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;

        options.SignIn.RequireConfirmedAccount.Should().BeFalse();
        options.User.RequireUniqueEmail.Should().BeTrue();
    }
}
