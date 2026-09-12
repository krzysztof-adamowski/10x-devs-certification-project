using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TenExCards.Components;
using TenExCards.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Deployed, this resolves an App Service setting holding a Key Vault reference; locally it comes
// from user-secrets, pointing at the DEVELOPMENT database. It is never in appsettings*.json.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not configured. Locally, set it with " +
        "`dotnet user-secrets set` (see TenExCards/AGENTS.md); deployed, it arrives as the app " +
        "setting ConnectionStrings__DefaultConnection resolving a Key Vault reference. Failing " +
        "here is deliberate: a null connection string would otherwise surface as an opaque " +
        "provider error during the startup migration below.");

// BOTH registrations are required, and this is not stylistic.
//
// AddDbContextFactory<AppDbContext> does NOT also register AppDbContext itself, and
// PersistKeysToDbContext<AppDbContext> resolves the context from a service scope with
// GetRequiredService — not from the factory. A factory-only registration compiles, starts, and
// then throws the first time anything protects data, which with UseAntiforgery() in the pipeline
// is the first rendered form.
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

// EnableRetryOnFailure above is not optional: Azure SQL produces transient faults, and a retry the
// application does not make becomes a user-visible failure against a 2s acknowledgement budget.

// Persists the Data Protection key ring to the database, so it survives a container restart. Before
// this, every restart rotated the ring — rejecting antiforgery tokens minted before it.
var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>();

// Encrypts that ring at rest. PERSISTENCE AND ENCRYPTION ARE DIFFERENT GUARANTEES: the call above
// makes the key survive a restart, and left to itself it writes DataProtectionKeys.Xml as readable
// plaintext. ASP.NET Core says so exactly once — `No XML encryptor configured`, logged only on the
// boot that MINTS a key and never again, which is why its absence from a log proves nothing unless
// that boot minted one.
//
// This matters more from S-01 onward than it did before it. The same ring now signs auth cookies,
// so database read access that previously bought antiforgery token forgery would otherwise buy
// session forgery for every account.
//
// THE GUARD IS DELIBERATE — do not remove it as an inconsistency. Local development points at
// sqldb-tenexcards-dev, whose ring protects nothing of value, and requiring vault key permissions on
// a development machine would widen access that is already wider than anyone likes. A developer
// without those permissions must still be able to run the app.
//
// The identifier is a POINTER, not a secret, so it arrives as a plain app setting rather than as a
// Key Vault reference — but it is still never in appsettings*.json. Its value comes from the
// `dataProtectionKeyUri` output of infra/main.bicep, which is what keeps the app setting and the
// template from drifting apart. CI never sets it, so it must already exist before a build carrying
// this code is deployed: reaching `new Uri(null)` here happens during service configuration, and the
// container then does not serve at all, on a tier with no deployment slots.
if (!builder.Environment.IsDevelopment())
{
    var keyIdentifier = builder.Configuration["DataProtection:KeyIdentifier"]
        ?? throw new InvalidOperationException(
            "DataProtection:KeyIdentifier is not configured. Outside Development the key ring is "
            + "encrypted with a Key Vault key, and this setting is the versionless identifier of "
            + "that key. Deployed, it arrives as the app setting DataProtection__KeyIdentifier, set "
            + "with `az webapp config appsettings set` from the dataProtectionKeyUri output of "
            + "infra/main.bicep. Failing here is deliberate: it names the missing setting instead of "
            + "surfacing later as an opaque `new Uri(null)` during service configuration.");

    dataProtection.ProtectKeysWithAzureKeyVault(new Uri(keyIdentifier), new DefaultAzureCredential());
}

var app = builder.Build();

// MIGRATIONS RUN ON THE BOOT PATH. If this throws, the container does not serve — on a B1 tier with
// no deployment slots, so there is no slot swap to roll back to. The rollback path is redeploying
// the retained previous archive, and that does NOT reverse schema: migrations here are forward-only
// and no down migration is authored. Treat every migration as one-way.
//
// The outcome is logged explicitly rather than left to an unhandled exception, and this sits after
// the logging pipeline is available so the log line actually goes somewhere.
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Database schema is current; no migrations to apply.");
        }
        else
        {
            logger.LogInformation("Applying {Count} pending migration(s): {Migrations}",
                pending.Count, string.Join(", ", pending));
            await db.Database.MigrateAsync();
            logger.LogInformation("Migrations applied successfully.");
        }
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Startup migration FAILED; the application cannot serve. The "
            + "rollback path is redeploying the previously retained archive — note that this does "
            + "not reverse schema.");
        throw;
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// No UseHttpsRedirection(): HTTPS is enforced at the platform, which declares httpsOnly: true in
// infra/main.bicep. That guarantee is what makes the absence safe -- reinstate the middleware if
// this app is ever deployed somewhere that cannot enforce it. See TenExCards/AGENTS.md "### HTTPS".

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
