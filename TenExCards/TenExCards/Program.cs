using Azure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TenExCards.Cards;
using TenExCards.Components;
using TenExCards.Components.Account;
using TenExCards.Data;
using TenExCards.Generation;

var builder = WebApplication.CreateBuilder(args);

// Debug-only, Development-only browser-test seam. Outside Debug this is a const false and the
// compiler removes every branch below, so the harness cannot exist in the deployed binary.
#if DEBUG
var e2e = TenExCards.Testing.E2EHarness.UseE2EHarness(builder);
#else
const bool e2e = false;
#endif

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

if (!e2e)
{
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
}

builder.Services.AddScoped<ICardStore, CardStore>();
// Outside the e2e guard with ICardStore: it rides the AppDbContext factory, which the
// harness swaps, so the browser suite records outcomes too.
builder.Services.AddScoped<ITriageRecorder, TriageRecorder>();

// EnableRetryOnFailure above is not optional: Azure SQL produces transient faults, and a retry the
// application does not make becomes a user-visible failure against a 2s acknowledgement budget.

// ValidateOnStart, so a bad pair fails at boot rather than at a learner's first submission —
// TargetCandidateCount clamps between these two and throws when the floor exceeds the cap.
builder.Services.AddOptions<GenerationOptions>()
    .Bind(builder.Configuration.GetSection(GenerationOptions.SectionName))
    .Validate(o => o.MinCandidates >= 1 && o.MinCandidates <= o.MaxCandidates,
        "Generation:MinCandidates must be at least 1 and no greater than Generation:MaxCandidates.")
    .Validate(o => o.MaxPassageCharacters > 0 && o.MaxFocusHintCharacters > 0 && o.WordsPerCandidate > 0,
        "Generation:MaxPassageCharacters, MaxFocusHintCharacters and WordsPerCandidate must be positive.")
    .Validate(o => o.TimeoutSeconds > 0, "Generation:TimeoutSeconds must be positive.")
    .ValidateOnStart();
builder.Services.Configure<GeminiOptions>(
    builder.Configuration.GetSection(GeminiOptions.SectionName));

// The cap on /cards. Validated at boot for the same reason as above: a zero here would render an
// empty saved-card list that looks like data loss.
builder.Services.AddOptions<CardOptions>()
    .Bind(builder.Configuration.GetSection(CardOptions.SectionName))
    .Validate(o => o.MaxResults > 0, "Cards:MaxResults must be positive.")
    .ValidateOnStart();

if (!e2e)
{
    // Unconditional, unlike the key-identifier guard below: a development machine does need a real API
    // key to generate anything. Read eagerly so the throw names the setting at boot.
    _ = builder.Configuration["Gemini:ApiKey"]
        ?? throw new InvalidOperationException(
            "Gemini:ApiKey is not configured. Locally it comes from user-secrets; deployed it arrives "
            + "as the app setting Gemini__ApiKey resolving the gemini-api-key Key Vault reference, "
            + "which must be set and Resolved BEFORE the build that reads it is merged.");

    // Eager, because the generator is a singleton and would otherwise fail at the first submission
    // rather than at boot.
    if (builder.Configuration.GetSection("Gemini:Models").Get<string[]>() is not { Length: > 0 })
    {
        throw new InvalidOperationException(
            "Gemini:Models is empty. It is the ordered model rotation, best quality first; each entry "
            + "has its own free-tier daily quota and the generator falls through on HTTP 429.");
    }

    // Singleton: it holds a thread-safe OpenAIClient and keeps no per-request state.
    builder.Services.AddSingleton<ICardCandidateGenerator, GeminiCardCandidateGenerator>();
}

// The cookie scheme is the default; there is no external login, so no DefaultSignInScheme override
// is needed for it. AddIdentityCookies() registers the handler LoginPath below configures.
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
    })
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    // Seven days, sliding: the PRD's session-inactivity window (see context/foundation/prd.md
    // "## Open Questions" item 1), renewed on activity rather than fixed from sign-in.
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
});

// NO AddDefaultTokenProviders() — DELIBERATE. It registers the email-confirmation, phone and
// authenticator token providers, which is exactly the surface `## What We're NOT Doing` forbids
// (no password recovery, no 2FA). Register, sign in, sign out and lockout all work without it.
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;

        // LENGTH OVER COMPOSITION — DELIBERATE, do not restore the character-class defaults. A
        // forgotten password is a permanently dead account here (no recovery), and sixteen
        // characters is what makes a stolen PasswordHash uneconomic to attack offline even though
        // database read access still returns that hash (see Phase 1's overview). Password
        // *storage* itself is left untouched below — inherited from AddIdentityCore, not written.
        options.Password.RequiredLength = 16;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

// Everything is protected unless it says otherwise: Home, Error, NotFound and the Identity pages
// carry [AllowAnonymous]. MapStaticAssets() and AddInteractiveServerRenderMode() below both map
// endpoints of their own that carry no such attribute, so they need it added explicitly below —
// otherwise every stylesheet and script 302s to the login path. See TenExCards/AGENTS.md.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

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
//
// GUARDED FOR THE TEST HARNESS ONLY — defaults to running the migration. WebApplicationFactory runs
// this entry point and intercepts at IHost.Start(), so this block executes in tests too, after
// ConfigureTestServices has already swapped in the EF in-memory provider; GetPendingMigrationsAsync
// throws against that provider. A missing setting must never silently skip a real migration, so the
// default is "run it" and only the test factory sets Testing:SkipStartupMigration to true.
// The E2E harness is an ADDITIONAL reason to skip — it never changes Testing:SkipStartupMigration's
// "run it" default, which is what stops a missing setting silently skipping a real migration.
if (!e2e && !builder.Configuration.GetValue<bool>("Testing:SkipStartupMigration"))
{
    using var scope = app.Services.CreateScope();
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

// Must come BEFORE UseAntiforgery(): antiforgery validation on a POST needs to know who the
// caller is first. ASPNETCORE_FORWARDEDHEADERS_ENABLED is already on by platform default and must
// stay unset — see TenExCards/AGENTS.md — so Request.IsHttps is already correct here and the auth
// cookie keeps `Secure` under the default CookieSecurePolicy.SameAsRequest.
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// The logout POST must be reachable whether or not the caller is authenticated (posting it twice,
// or after the cookie already expired, must not itself 302 to login), mapped after
// MapRazorComponents<App>() per the .NET Identity template's own convention for account endpoints.
app.MapIdentityLogout().AllowAnonymous();

app.Run();

// Top-level statements generate an internal Program class. TenExCards.Tests depends on this
// declaration to name the entry point for WebApplicationFactory<Program> — do not remove it.
public partial class Program;
