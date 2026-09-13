using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TenExCards.Data;
using TenExCards.Generation;

namespace TenExCards.Tests;

/// <summary>
/// The triage outcome record: counters, the account boundary around them, and the best-effort
/// contract that keeps measurement off the learner's path.
/// </summary>
public class TriageRecorderTests(TenExCardsWebApplicationFactory factory)
    : IClassFixture<TenExCardsWebApplicationFactory>
{
    private const string ValidPassword = "CorrectHorseBattery16";

    private async Task<string> CreateUserAsync()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"triage-{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser { UserName = email, Email = email };
        var result = await userManager.CreateAsync(user, ValidPassword);
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Description)));
        return user.Id;
    }

    private async Task<T> WithRecorderAsync<T>(Func<ITriageRecorder, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ITriageRecorder>());
    }

    private async Task WithRecorderAsync(Func<ITriageRecorder, Task> work)
    {
        using var scope = factory.Services.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<ITriageRecorder>());
    }

    private async Task<TriageBatch?> ReadBatchAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TriageBatches.SingleOrDefaultAsync(b => b.Id == id);
    }

    [Fact]
    public async Task OpenBatchAsync_PersistsTheBatchSizeAndZeroedCounters()
    {
        var owner = await CreateUserAsync();

        var before = DateTimeOffset.UtcNow;
        var id = await WithRecorderAsync(r => r.OpenBatchAsync(owner, 5, CancellationToken.None));

        id.Should().NotBeNull();
        var batch = await ReadBatchAsync(id!.Value);
        batch.Should().NotBeNull();
        batch!.OwnerId.Should().Be(owner);
        batch.CandidateCount.Should().Be(5);
        batch.AcceptedCount.Should().Be(0);
        batch.RejectedCount.Should().Be(0);
        batch.EditedCount.Should().Be(0);
        // Result set 2 of outcome_rates.sql filters on this; a default value would call every
        // batch settled.
        batch.OpenedAt.Should().BeOnOrAfter(before);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EveryMember_WithNoOwner_RecordsNothingAndDoesNotThrow(string ownerId)
    {
        // The guard sits inside the swallow, unlike CardStore's: the reject path has no try/catch
        // of its own, so throwing here would take the untriaged batch with it.
        var open = () => WithRecorderAsync(r => r.OpenBatchAsync(ownerId, 3, CancellationToken.None));
        var accept = () => WithRecorderAsync(r => r.RecordAcceptAsync(ownerId, Guid.NewGuid(), edited: false, CancellationToken.None));
        var reject = () => WithRecorderAsync(r => r.RecordRejectAsync(ownerId, Guid.NewGuid(), CancellationToken.None));

        (await open.Should().NotThrowAsync()).Which.Should().BeNull();
        await accept.Should().NotThrowAsync();
        await reject.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RecordAcceptAsync_IncrementsAcceptedAndOnlyCountsTheEditedOnes()
    {
        var owner = await CreateUserAsync();
        var id = await WithRecorderAsync(r => r.OpenBatchAsync(owner, 3, CancellationToken.None));

        await WithRecorderAsync(r => r.RecordAcceptAsync(owner, id, edited: false, CancellationToken.None));
        await WithRecorderAsync(r => r.RecordAcceptAsync(owner, id, edited: true, CancellationToken.None));

        var batch = await ReadBatchAsync(id!.Value);
        batch!.AcceptedCount.Should().Be(2);
        batch.EditedCount.Should().Be(1, "EditedCount counts rewordings, not acceptances");
        batch.RejectedCount.Should().Be(0);
    }

    [Fact]
    public async Task RecordRejectAsync_IncrementsRejectedOnly()
    {
        var owner = await CreateUserAsync();
        var id = await WithRecorderAsync(r => r.OpenBatchAsync(owner, 3, CancellationToken.None));

        await WithRecorderAsync(r => r.RecordRejectAsync(owner, id, CancellationToken.None));

        var batch = await ReadBatchAsync(id!.Value);
        batch!.RejectedCount.Should().Be(1);
        batch.AcceptedCount.Should().Be(0);
        batch.EditedCount.Should().Be(0);
    }

    [Fact]
    public async Task RecordAsync_WithAnotherAccountsBatchId_MovesNothing()
    {
        var ownerA = await CreateUserAsync();
        var ownerB = await CreateUserAsync();
        var id = await WithRecorderAsync(r => r.OpenBatchAsync(ownerA, 4, CancellationToken.None));

        await WithRecorderAsync(r => r.RecordAcceptAsync(ownerB, id, edited: true, CancellationToken.None));
        await WithRecorderAsync(r => r.RecordRejectAsync(ownerB, id, CancellationToken.None));

        var batch = await ReadBatchAsync(id!.Value);
        batch!.AcceptedCount.Should().Be(0, "a batch id that is not yours must move nothing");
        batch.EditedCount.Should().Be(0);
        batch.RejectedCount.Should().Be(0);
    }

    [Fact]
    public async Task RecordAsync_WithNoBatchId_IsANoOp()
    {
        var owner = await CreateUserAsync();

        // The open failed and answered null; triage still has to work.
        var accept = () => WithRecorderAsync(r => r.RecordAcceptAsync(owner, null, edited: true, CancellationToken.None));
        var reject = () => WithRecorderAsync(r => r.RecordRejectAsync(owner, null, CancellationToken.None));

        await accept.Should().NotThrowAsync();
        await reject.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EveryMember_WhenTheDatabaseIsUnreachable_SwallowsRatherThanBreakingTriage()
    {
        var recorder = new TriageRecorder(new ThrowingDbContextFactory(), NullLogger<TriageRecorder>.Instance);

        var id = await recorder.OpenBatchAsync("owner-1", 5, CancellationToken.None);
        id.Should().BeNull("a failed open must answer null so the record calls no-op");

        var accept = () => recorder.RecordAcceptAsync("owner-1", Guid.NewGuid(), edited: true, CancellationToken.None);
        var reject = () => recorder.RecordRejectAsync("owner-1", Guid.NewGuid(), CancellationToken.None);

        await accept.Should().NotThrowAsync("measurement must never cost a learner a card");
        await reject.Should().NotThrowAsync();
    }

    [Fact]
    public void ITriageRecorder_ResolvesFromTheRealPipeline()
    {
        // Catches a registration dropped inside Program.cs's !e2e guard, which would compile and
        // then throw at the first triage.
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITriageRecorder>().Should().BeOfType<TriageRecorder>();
    }

    [Fact]
    public async Task EveryMember_WhenTheDatabaseHangs_ReturnsOnItsOwnBoundNotEfsRetryBudget()
    {
        // The shared factory retries for ~30s. Two unbounded calls would cost the learner a minute
        // behind the _busy guard, on paths that show no progress at all.
        var recorder = new TriageRecorder(new HangingDbContextFactory(), NullLogger<TriageRecorder>.Instance);
        var elapsed = Stopwatch.StartNew();

        var id = await recorder.OpenBatchAsync("owner-1", 5, CancellationToken.None);
        await recorder.RecordAcceptAsync("owner-1", Guid.NewGuid(), edited: false, CancellationToken.None);

        elapsed.Stop();
        id.Should().BeNull();
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15),
            "two calls bounded at 2s each must not wait on the full retry budget");
    }

    private sealed class ThrowingDbContextFactory : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => throw new InvalidOperationException("no database");
    }

    /// <summary>Never answers; only the recorder's own bound ends the wait.</summary>
    private sealed class HangingDbContextFactory : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => throw new NotSupportedException();

        public async Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(60), ct);
            throw new InvalidOperationException("unreachable");
        }
    }
}
