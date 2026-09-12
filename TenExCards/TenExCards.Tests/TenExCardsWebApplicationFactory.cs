using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenExCards.Data;

namespace TenExCards.Tests;

/// <summary>
/// Boots the real <c>Program.cs</c> pipeline against an isolated EF in-memory database, with no
/// real connection string, no boot-path migration, and no Key Vault dependency.
/// </summary>
/// <remarks>
/// A fresh, uniquely-named in-memory database per factory instance — <c>IClassFixture&lt;&gt;</c>
/// creates one instance per test class, so tests in different classes never share state, and
/// tests within one class share a database the way they would share a real one.
/// </remarks>
public class TenExCardsWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"TenExCardsTests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs throws on a null connection string before any service replacement below
        // can run, so a dummy value must exist even though nothing ever reads it for real.
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=unused;Database=unused;");

        // The only way to stop the boot-path migration — see Program.cs's comment on this flag.
        builder.UseSetting("Testing:SkipStartupMigration", "true");

        // Belt-and-braces, confirmed by measurement rather than assumed (see
        // IdentityConfigurationTests.WebApplicationFactory_defaults_to_Development_on_its_own):
        // WebApplicationFactory already sets ASPNETCORE_ENVIRONMENT to Development on its own,
        // so Program.cs's `if (!builder.Environment.IsDevelopment())` Key Vault guard already
        // skips without this. Kept explicit so a future framework change to that default cannot
        // silently make this factory start reaching Key Vault.
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureServices(services =>
        {
            // Replace BOTH AppDbContext registrations — the factory and the scoped shim — per
            // Program.cs's own comment on why both exist (PersistKeysToDbContext resolves from
            // a scope with GetRequiredService, not from the factory).
            //
            // RemoveAll<DbContextOptions<AppDbContext>> alone is not enough: AddDbContextFactory
            // also registers the options-configuring action as its own
            // IDbContextOptionsConfiguration<AppDbContext> DI entry, which DbContextOptionsFactory
            // applies ADDITIVELY alongside any later one. Leaving Program.cs's SqlServer entry in
            // place while adding an InMemory one here fails at first use with "Services for
            // database providers ... have been registered" — EF sees both providers configured on
            // the same options. Filtering by generic argument removes that entry too without
            // needing to name its (internal-ish) interface type.
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
                options.UseInMemoryDatabase(_databaseName));
            services.AddScoped(sp =>
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        });
    }
}
