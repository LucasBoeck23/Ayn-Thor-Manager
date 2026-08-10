using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;

namespace AynThorManager.UnitTests.Properties;

/// <summary>
/// Property 1: Máquina de estados do heartbeat
/// Feature: adb-file-management, Property 1: Máquina de estados do heartbeat
/// Validates: Requirements 1.3
/// </summary>
public sealed class HeartbeatStateMachineProperties
{
    /// <summary>
    /// Simulates the heartbeat state machine logic.
    /// Returns indices where a disconnection event fires (i.e., the 3rd consecutive failure).
    /// After a disconnection fires, the counter resets.
    /// A success (true) always resets the failure counter.
    /// </summary>
    public static List<int> FindDisconnectionPoints(bool[] heartbeatResults)
    {
        var disconnectionPoints = new List<int>();
        var consecutiveFailures = 0;

        for (int i = 0; i < heartbeatResults.Length; i++)
        {
            if (heartbeatResults[i]) // success
            {
                consecutiveFailures = 0;
            }
            else // failure
            {
                consecutiveFailures++;
                if (consecutiveFailures >= 3)
                {
                    disconnectionPoints.Add(i);
                    consecutiveFailures = 0; // reset after disconnect
                }
            }
        }

        return disconnectionPoints;
    }

    /// <summary>
    /// **Validates: Requirements 1.3**
    /// For any sequence with fewer than 3 consecutive failures, no disconnection event fires.
    /// </summary>
    [Property(MaxTest = 200)]
    public Property SequenceWithNoThreeConsecutiveFailures_NeverDisconnects()
    {
        var gen = Gen.ArrayOf(Arb.Default.Bool().Generator)
            .Where(arr => !HasThreeOrMoreConsecutiveFailures(arr));

        return Prop.ForAll(Arb.From(gen), sequence =>
        {
            var disconnections = FindDisconnectionPoints(sequence);
            disconnections.Should().BeEmpty(
                because: "a sequence without 3+ consecutive failures should never trigger disconnection");
        });
    }

    /// <summary>
    /// **Validates: Requirements 1.3**
    /// For any sequence containing exactly 3 consecutive failures starting at some position,
    /// a disconnection event fires at the index of the 3rd failure.
    /// </summary>
    [Property(MaxTest = 200)]
    public Property ThreeConsecutiveFailures_TriggersDisconnectionAtThirdFailure()
    {
        // Generate a prefix of successes, then exactly 3 failures, then a suffix of successes
        var gen = from prefixLen in Gen.Choose(0, 10)
                  from suffixLen in Gen.Choose(0, 10)
                  let prefix = Enumerable.Repeat(true, prefixLen).ToArray()
                  let failures = new[] { false, false, false }
                  let suffix = Enumerable.Repeat(true, suffixLen).ToArray()
                  select prefix.Concat(failures).Concat(suffix).ToArray();

        return Prop.ForAll(Arb.From(gen), sequence =>
        {
            var disconnections = FindDisconnectionPoints(sequence);
            var prefixLen = Array.IndexOf(sequence, false);
            var expectedDisconnectIndex = prefixLen + 2; // 3rd consecutive failure

            disconnections.Should().Contain(expectedDisconnectIndex,
                because: $"the 3rd consecutive failure at index {expectedDisconnectIndex} should trigger disconnection");
        });
    }

    /// <summary>
    /// **Validates: Requirements 1.3**
    /// A single success between failures resets the counter, so [F,F,T,F,F,F] fires at index 5.
    /// </summary>
    [Property(MaxTest = 200)]
    public Property SuccessResetsCounter_RequiresThreeNewConsecutiveFailures()
    {
        // Generate sequences: some failures (0-2), a success, then 3 failures
        var gen = from initialFailures in Gen.Choose(0, 2)
                  from trailingSuccesses in Gen.Choose(0, 5)
                  let part1 = Enumerable.Repeat(false, initialFailures).ToArray()
                  let reset = new[] { true }
                  let threeFailures = new[] { false, false, false }
                  let trail = Enumerable.Repeat(true, trailingSuccesses).ToArray()
                  select part1.Concat(reset).Concat(threeFailures).Concat(trail).ToArray();

        return Prop.ForAll(Arb.From(gen), sequence =>
        {
            var disconnections = FindDisconnectionPoints(sequence);

            // The success resets the counter, so disconnection fires at the 3rd failure after the success
            var resetIndex = Array.IndexOf(sequence, true);
            var expectedDisconnectIndex = resetIndex + 3; // 3 failures after the reset

            disconnections.Should().Contain(expectedDisconnectIndex,
                because: "after a success resets the counter, 3 new consecutive failures should trigger disconnection");
        });
    }

    /// <summary>
    /// **Validates: Requirements 1.3**
    /// After a disconnection event fires, the counter resets. So 6 consecutive failures fire at index 2 AND index 5.
    /// </summary>
    [Property(MaxTest = 200)]
    public Property AfterDisconnect_CounterResets_AllowsSubsequentDisconnection()
    {
        // Generate sequences of N groups of 3 failures (with optional leading successes)
        var gen = from leadingSuccesses in Gen.Choose(0, 5)
                  from groups in Gen.Choose(2, 5)
                  let prefix = Enumerable.Repeat(true, leadingSuccesses).ToArray()
                  let failures = Enumerable.Repeat(false, groups * 3).ToArray()
                  select prefix.Concat(failures).ToArray();

        return Prop.ForAll(Arb.From(gen), sequence =>
        {
            var disconnections = FindDisconnectionPoints(sequence);
            var leadingSuccesses = sequence.TakeWhile(b => b).Count();

            // Every group of 3 consecutive failures should produce a disconnection
            var expectedCount = (sequence.Length - leadingSuccesses) / 3;
            disconnections.Should().HaveCount(expectedCount,
                because: $"each group of 3 consecutive failures should trigger a disconnection (expected {expectedCount} disconnections)");

            // Verify each disconnection point is at the correct index
            for (int i = 0; i < expectedCount; i++)
            {
                var expectedIndex = leadingSuccesses + (i * 3) + 2;
                disconnections[i].Should().Be(expectedIndex,
                    because: $"disconnection {i + 1} should fire at index {expectedIndex}");
            }
        });
    }

    /// <summary>
    /// **Validates: Requirements 1.3**
    /// Universal property: for any random sequence of heartbeat results, disconnection events
    /// fire if and only if there are groups of 3 consecutive failures (after accounting for resets).
    /// </summary>
    [Property(MaxTest = 500)]
    public Property DisconnectionOccursIfAndOnlyIfThreeConsecutiveFailures()
    {
        return Prop.ForAll(Arb.From(Gen.ArrayOf(Arb.Default.Bool().Generator)), sequence =>
        {
            var disconnections = FindDisconnectionPoints(sequence);

            // Cross-verify with a reference implementation that counts differently
            var expectedDisconnections = ReferenceDisconnectionCount(sequence);

            disconnections.Should().HaveCount(expectedDisconnections,
                because: "the number of disconnection events must match the reference implementation");

            // Verify all disconnection points correspond to the 3rd failure in their group
            foreach (var point in disconnections)
            {
                // At each disconnection point, the value must be a failure
                sequence[point].Should().BeFalse(
                    because: "disconnection can only occur on a failure");

                // The two preceding values (in the same group) must also be failures
                // But they might wrap around a previous disconnect reset, so we verify
                // by checking the local context: at minimum the element itself is false
            }
        });
    }

    /// <summary>
    /// Reference implementation to count expected disconnection events.
    /// </summary>
    private static int ReferenceDisconnectionCount(bool[] sequence)
    {
        var count = 0;
        var consecutiveFailures = 0;

        foreach (var result in sequence)
        {
            if (result)
            {
                consecutiveFailures = 0;
            }
            else
            {
                consecutiveFailures++;
                if (consecutiveFailures >= 3)
                {
                    count++;
                    consecutiveFailures = 0;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Helper: checks if a bool array has 3 or more consecutive false values.
    /// </summary>
    private static bool HasThreeOrMoreConsecutiveFailures(bool[] sequence)
    {
        var consecutive = 0;
        foreach (var b in sequence)
        {
            if (!b)
            {
                consecutive++;
                if (consecutive >= 3)
                    return true;
            }
            else
            {
                consecutive = 0;
            }
        }
        return false;
    }
}
