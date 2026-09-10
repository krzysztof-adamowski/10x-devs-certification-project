using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace TenExCards.Data;

/// <summary>
/// The single context every later slice extends. F-02 gives it only what the persistence spine
/// needs: the disposable probe entity and the Data Protection key set.
/// </summary>
/// <remarks>
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
    : DbContext(options), IDataProtectionKeyContext
{
    /// <summary>
    /// Required by <see cref="IDataProtectionKeyContext"/>. The key ring persists here, so it
    /// survives a container restart — before F-02 every restart rotated it, which logged users out
    /// and rejected antiforgery tokens. <c>S-01</c> verifies this rather than implementing it.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>Throwaway. Deleted by <c>S-01</c> along with its table. See <see cref="SpineProbe"/>.</summary>
    public DbSet<SpineProbe> SpineProbes => Set<SpineProbe>();
}
