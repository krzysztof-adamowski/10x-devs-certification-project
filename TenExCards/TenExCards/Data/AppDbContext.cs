using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace TenExCards.Data;

/// <summary>
/// The single context every later slice extends. F-02 gave it the disposable probe entity and the
/// Data Protection key set; S-01 adds Identity's user store on top.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IdentityUserContext{TUser}"/> rather than <see cref="Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext{TUser}"/>
/// is deliberate: it creates no role tables, which makes AGENTS.md's "never add roles" structural
/// rather than conventional — <c>AddRoles&lt;&gt;()</c> fails against this base by design.
/// </para>
/// <para>
/// <see cref="IDataProtectionKeyContext"/> is implemented here rather than on a separate context
/// on purpose: it is what lets <c>PersistKeysToDbContext&lt;AppDbContext&gt;()</c> target this
/// context. Without the interface that call does not compile.
/// </para>
/// <para>
/// The Data Protection key table has no per-application partition — every key row for every app
/// pointed at this database lands in one table. That is why local development runs against a
/// separate database (<c>sqldb-tenexcards-dev</c>) rather than the one the live site serves from,
/// and why the connection string in user-secrets must never be edited to point at the app's
/// database. See the comment block at the top of <c>infra/main.bicep</c>.
/// </para>
/// </remarks>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityUserContext<ApplicationUser>(options), IDataProtectionKeyContext
{
    /// <summary>
    /// Required by <see cref="IDataProtectionKeyContext"/>. The key ring persists here, so it
    /// survives a container restart — before F-02 every restart rotated it, which logged users out
    /// and rejected antiforgery tokens. <c>S-01</c> verifies this rather than implementing it.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>
    /// The product's cards (S-02). Query this through <c>TenExCards.Cards.ICardStore</c> rather
    /// than directly: the account filter is a property of that type, not a discipline every caller
    /// has to remember.
    /// </summary>
    public DbSet<Card> Cards => Set<Card>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // FIRST — Identity's own configuration depends on it.
        base.OnModelCreating(builder);

        builder.Entity<Card>(card =>
        {
            // THESE TWO LENGTHS ARE LOAD-BEARING IN TWO PLACES, so neither is free to change alone.
            // They bound the circuit's memory budget (a full untriaged batch is held in Blazor
            // Server memory until triage ends), and S-02's generator enforces them on the model's
            // output before SaveAsync — nothing else stands between a generated candidate and this
            // table, and a truncation error here throws inside a circuit event handler, losing the
            // whole untriaged batch behind the generic Blazor error UI. The EF in-memory provider
            // does NOT enforce HasMaxLength, so no factory-booted test can catch a drift; the
            // generator's own limits are asserted equal to these values instead.
            card.Property(c => c.Prompt).IsRequired().HasMaxLength(500);
            card.Property(c => c.Answer).IsRequired().HasMaxLength(1000);

            card.Property(c => c.OwnerId).IsRequired();
            card.HasIndex(c => c.OwnerId);

            // No navigation property on ApplicationUser: that type is deliberately empty, and the
            // relationship needs no collection to exist. Cascade delete means deleting an account
            // takes its cards with it rather than leaving orphan rows pointing at a missing owner.
            card.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(c => c.OwnerId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
