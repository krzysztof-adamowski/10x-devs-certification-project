using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenExCards.Data;

namespace TenExCards.Tests;

/// <summary>
/// Proves each boot-path guard fires for its own reason, so a setting the deployed container
/// demands is caught at Test rather than as a container that will not serve — on a tier with no
/// deployment slot to roll back to.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion matches the guard's MESSAGE, never the exception type. Four guards throw
/// InvalidOperationException during service configuration, so a type-only assertion is satisfied
/// by whichever throws first and proves nothing — the near-miss <see cref="MigrationGuardTests"/>
/// was narrowed for.
/// </para>
/// <para>
/// THIS HOST IS NEVER GIVEN DataProtection:KeyIdentifier. Measured 2026-09-14: supplying it makes
/// the host construct DefaultAzureCredential, reach kv-tenexcards-plc and fail keys/wrap/action
/// with ForbiddenByRbac, using the operator's own az session. Boot and GET / both still succeed,
/// so that failure is silent to a status-code observer. The encryptor itself stays with the manual
/// Xml-column verification in TenExCards/AGENTS.md.
/// </para>
/// </remarks>
public class BootPathGuardTests
{
    private const string DummyConnectionString = "Server=unused;Database=unused;";
    private const string DummyApiKey = "test-key-not-a-real-credential";

    /// <summary>
    /// A bare factory with every guarded setting supplied except those named in
    /// <paramref name="omit"/>.
    /// </summary>
    /// <remarks>
    /// Always Production, and that is load-bearing rather than incidental: WebApplication.
    /// CreateBuilder loads user-secrets ONLY in Development, so a Development host would satisfy
    /// every omitted setting from the developer's own secrets.json — these tests would pass in CI
    /// and fail locally. Production also matches the shape the guards actually protect.
    /// Environments.Development appears below only where the environment IS the variable under
    /// test. See TenExCards.Tests/AGENTS.md, "A green local run is not a green CI run".
    /// </remarks>
    private static WebApplicationFactory<Program> BuildHost(
        string? environment = null, params string[] omit)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment ?? Environments.Production);

            if (!omit.Contains("ConnectionStrings:DefaultConnection"))
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", DummyConnectionString);
            }

            if (!omit.Contains("Gemini:ApiKey"))
            {
                builder.UseSetting("Gemini:ApiKey", DummyApiKey);
            }

            builder.UseSetting("Testing:SkipStartupMigration", "true");

            builder.ConfigureServices(services =>
            {
                // Filter by generic argument: AddDbContextFactory also registers its
                // options-configuring delegate, which EF applies additively. See the same
                // comment in TenExCardsWebApplicationFactory.
                var descriptors = services
                    .Where(d => d.ServiceType == typeof(AppDbContext)
                        || (d.ServiceType.IsGenericType
                            && d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext))))
                    .ToList();
                foreach (var descriptor in descriptors)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContextFactory<AppDbContext>(options =>
                    options.UseInMemoryDatabase($"BootPathGuard-{Guid.NewGuid()}"));
                services.AddScoped(sp =>
                    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
            });
        });
    }

    [Theory]
    [InlineData("ConnectionStrings:DefaultConnection",
        "*ConnectionStrings:DefaultConnection is not configured*")]
    [InlineData("Gemini:ApiKey", "*Gemini:ApiKey is not configured*")]
    public void MissingDemand_FailsBootNamingItself(string setting, string expectedMessage)
    {
        using var factory = BuildHost(omit: setting);

        var act = () => _ = factory.Services;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage(expectedMessage,
                "the guard for {0} must be what threw, not another guard of the same type",
                setting);
    }

    [Fact]
    public void ShippedModelRotation_IsNotEmpty()
    {
        // Gemini:Models ships in appsettings.json, so no test host can make it ABSENT — the
        // configuration system has no deletion. The guard's real trigger is someone emptying that
        // array, which fails the deployed boot, so the assertion is on the shipped value.
        // Development, because this host must actually BOOT and Production demands the key
        // identifier this class never supplies.
        using var factory = BuildHost(Environments.Development);
        using var client = factory.CreateClient();

        var models = factory.Services.GetRequiredService<IConfiguration>()
            .GetSection("Gemini:Models").Get<string[]>();

        models.Should().NotBeNullOrEmpty(
            "an empty rotation throws at builder configuration and the container does not serve; "
            + "the entries are also the free tier's per-model daily quota, not redundancy");
    }

    [Fact]
    public void KeyIdentifier_Missing_OutsideDevelopment_FailsBoot()
    {
        // D6 is exercised in none of the three environments: Development-skipped locally,
        // Development-skipped in CI because TenExCardsWebApplicationFactory pins Development, and
        // live only in the container. This is the only place it runs.
        using var factory = BuildHost();

        var act = () => _ = factory.Services;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DataProtection:KeyIdentifier is not configured*",
                "outside Development the key ring is encrypted with a Key Vault key, and the "
                + "setting must exist before the build that reads it is merged");
    }

    [Fact]
    public void KeyIdentifier_Missing_InDevelopment_BootsAnyway()
    {
        // The control for the fact above, in the same run: one variable changed, the environment.
        // Without it, a throw from any other cause would satisfy that assertion. See lessons.md,
        // "A negative check needs a control, or it cannot fail".
        using var factory = BuildHost(Environments.Development);

        var act = () => _ = factory.Services;

        act.Should().NotThrow(
            "the Development guard is deliberate — a developer without vault key permissions must "
            + "still be able to run the app against sqldb-tenexcards-dev");
    }
}
