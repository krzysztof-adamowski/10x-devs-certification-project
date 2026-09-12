using System.Reflection;

namespace TenExCards.Components.Layout;

public partial class MainLayout
{
    // Makes "which build is live" answerable by looking rather than assumed. Two deploys of
    // byte-identical source are indistinguishable without it -- which is exactly why Phase 4's
    // criterion 4.8 could not be checked and was deferred to this phase.
    //
    // CI publishes with -p:SourceRevisionId=<sha>; the SDK appends that to
    // AssemblyInformationalVersion after a '+'. A local build sets no revision id, so it shows
    // "local" rather than a stale or invented SHA.
    private static readonly string BuildMarker = ResolveBuildMarker();

    private static string ResolveBuildMarker()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return "unknown";
        }

        var plus = informational.IndexOf('+');
        if (plus < 0 || plus == informational.Length - 1)
        {
            return "local";
        }

        var revision = informational[(plus + 1)..];
        return revision.Length > 7 ? revision[..7] : revision;
    }
}
