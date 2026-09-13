#if DEBUG
using Microsoft.EntityFrameworkCore;
using TenExCards.Data;
using TenExCards.Generation;

namespace TenExCards.Testing;

/// <summary>
/// Lets a locally started application serve the whole learner flow with no model, no database and
/// no Key Vault, so a browser test can drive the real pages deterministically.
///
/// Two things keep this out of production and both are required: the #if DEBUG around this file,
/// and the Debug-only Condition on the Microsoft.EntityFrameworkCore.InMemory package reference.
/// CI publishes with -c Release, so neither the provider nor this code reaches the archive.
/// </summary>
public static class E2EHarness
{
    /// <summary>
    /// Registers the scripted generator and an in-memory store, or does nothing. Returns whether it
    /// took over, so Program.cs can skip the real registrations it replaces.
    /// </summary>
    public static bool UseE2EHarness(this WebApplicationBuilder builder)
    {
        // Both conditions, not either: the flag alone must not be enough outside Development.
        if (!builder.Environment.IsDevelopment()
            || !builder.Configuration.GetValue<bool>("Testing:E2E"))
        {
            return false;
        }

        var databaseName = $"e2e-{Guid.NewGuid():N}";

        builder.Services.AddDbContextFactory<AppDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));

        // The Data Protection key store resolves AppDbContext from a scope, not from the factory.
        builder.Services.AddScoped(sp =>
            sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

        builder.Services.AddSingleton<ICardCandidateGenerator, ScriptedCardCandidateGenerator>();

        return true;
    }
}
#endif
