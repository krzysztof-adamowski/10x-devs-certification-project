using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TenExCards.Data;
using TenExCards.Generation;

namespace TenExCards.Tests;

/// <summary>
/// Proves each boot-path guard fires for its own reason, so a setting the deployed container
/// demands is caught at Test rather than as a container that will not serve — on a tier with no
/// deployment slot to roll back to.
/// </summary>
/// <remarks>
/// Every assertion matches the guard's MESSAGE, never the exception type: four guards throw
/// InvalidOperationException during service configuration, so a type-only assertion is satisfied
/// by whichever throws first — the near-miss <see cref="MigrationGuardTests"/> was narrowed for.
/// This host is never given DataProtection:KeyIdentifier, because supplying it reaches Key Vault
/// with the operator's own credentials; see TenExCards.Tests/AGENTS.md.
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
    /// Production by default, so user-secrets cannot satisfy an omitted setting; Development
    /// appears only where the environment IS the variable under test. See
    /// TenExCards.Tests/AGENTS.md, "A green local run is not a green CI run".
    /// </remarks>
    private static WebApplicationFactory<Program> BuildHost(
        string? environment = null, params string[] omit)
    {
        // Environment variables ARE loaded outside Development, so an ambient value here would
        // satisfy the guard and send the host to Key Vault. Fail naming the cause instead.
        if (Environment.GetEnvironmentVariable("DataProtection__KeyIdentifier") is not null)
        {
            throw new InvalidOperationException(
                "DataProtection__KeyIdentifier is set in this process's environment; unset it "
                + "before running these tests.");
        }

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

                // Inert for the guard tests, which throw before this runs — but the one test
                // here that boots would otherwise hold the real Gemini client, and a later test
                // that renders a page would spend live quota. Mirrors the shared factory.
                services.RemoveAll<ICardCandidateGenerator>();
                services.AddSingleton<ICardCandidateGenerator, StubCardCandidateGenerator>();
            });
        });
    }

    [Theory]
    [InlineData("ConnectionStrings:DefaultConnection",
        "*ConnectionStrings:DefaultConnection is not configured*")]
    [InlineData("Gemini:ApiKey", "*Gemini:ApiKey is not configured*")]
    public void GuardedSetting_Missing_FailsBootNamingItself(string setting, string expectedMessage)
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
        // Gemini:Models ships in appsettings.json and configuration has no deletion, so no host
        // can make it absent — this asserts the shipped value instead. It does NOT pin the guard;
        // see test-plan.md §7. Development because this host must actually boot.
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
        // D6 runs nowhere else: Development-skipped locally and in CI, live only in the
        // container.
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
        // The control for the fact above: same run, one variable changed. Without it a throw
        // from any other cause would satisfy that assertion.
        using var factory = BuildHost(Environments.Development);

        var act = () => _ = factory.Services;

        act.Should().NotThrow(
            "the Development guard is deliberate — a developer without vault key permissions must "
            + "still be able to run the app against sqldb-tenexcards-dev");
    }
}
