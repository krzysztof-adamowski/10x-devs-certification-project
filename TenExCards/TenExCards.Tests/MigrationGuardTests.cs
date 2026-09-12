using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenExCards.Data;

namespace TenExCards.Tests;

/// <summary>
/// Proves the migration-skip flag defaults to running the migration — a missing setting must
/// never silently skip a real one. Deliberately does NOT use
/// <see cref="TenExCardsWebApplicationFactory"/>, which always sets the flag; this asserts the
/// opposite case, when it is left unset.
/// </summary>
public class MigrationGuardTests
{
    [Fact]
    public void MigrationSkipFlag_Unset_RunsMigrationPath()
    {
        // Program.cs's boot-path migration block calls GetPendingMigrationsAsync(), which throws
        // against the EF in-memory provider. With Testing:SkipStartupMigration left unset, that
        // block executes (its default is to run), so accessing Services — which forces
        // WebApplicationFactory to build the host — must throw. A silently-skipped migration
        // would instead let this succeed.
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=unused;Database=unused;");
            // Testing:SkipStartupMigration is deliberately NOT set here.

            builder.ConfigureServices(services =>
            {
                var appDbContextDescriptors = services
                    .Where(d => d.ServiceType == typeof(AppDbContext)
                        || (d.ServiceType.IsGenericType
                            && d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext))))
                    .ToList();
                foreach (var descriptor in appDbContextDescriptors)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContextFactory<AppDbContext>(options =>
                    options.UseInMemoryDatabase($"TenExCardsTests-MigrationGuard-{Guid.NewGuid()}"));
                services.AddScoped(sp =>
                    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
            });
        });

        var act = () => _ = factory.Services;

        act.Should().Throw<InvalidOperationException>();
    }
}
