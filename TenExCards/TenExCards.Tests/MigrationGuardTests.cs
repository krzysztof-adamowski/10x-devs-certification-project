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

            // Without this, the Gemini:ApiKey guard throws the same type earlier and the assertion
            // below passes while proving nothing about migrations.
            builder.UseSetting("Gemini:ApiKey", "test-key-not-a-real-credential");

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

        // Message, not just type: two guards now throw InvalidOperationException here. String
        // observed from EF on 2026-09-13, not copied from docs.
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Relational-specific methods can only be used*",
                "the migration block must be what threw, not the Gemini:ApiKey guard");
    }
}
