namespace TenExCards.Tests;

/// <summary>
/// Temporary — proves criterion 4.10: a failing test blocks the deploy. Deleted in the very next
/// commit once CI confirms the run fails at the Test step and Deploy never runs.
/// </summary>
public class _DeliberatelyFailingTest
{
    [Fact]
    public void Deliberately_fails_to_prove_the_CI_gate_blocks_deploy()
    {
        Assert.Fail("Deliberate failure for criterion 4.10 — this test and this file are reverted immediately after CI confirms the gate.");
    }
}
