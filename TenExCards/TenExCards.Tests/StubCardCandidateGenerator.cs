using TenExCards.Generation;

namespace TenExCards.Tests;

/// <summary>
/// Registered in the factory, so no factory-booted test can reach the network.
/// </summary>
public class StubCardCandidateGenerator : ICardCandidateGenerator
{
    public static readonly IReadOnlyList<CandidateCard> Candidates =
    [
        new("What does a contained database user authenticate against?",
            "The database named in its own connection string."),
        new("Which deployment mode deletes resources absent from the template?",
            "Complete mode."),
        new("What persists the Data Protection key ring in this application?",
            "PersistKeysToDbContext against AppDbContext."),
    ];

    public Task<GenerationResult> GenerateAsync(
        string passage,
        string? focusHint,
        IProgress<GenerationProgress> progress,
        CancellationToken ct)
    {
        progress.Report(new GenerationProgress(1));
        return Task.FromResult(GenerationResult.Success(Candidates));
    }
}
