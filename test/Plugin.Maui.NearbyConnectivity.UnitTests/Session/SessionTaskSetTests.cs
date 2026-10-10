using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Covers <see cref="SessionTaskSet"/> — the owner of live session tasks. Stop and
/// Dispose join it: the join loops over tasks added mid-join, gives up at its bound, and never
/// surfaces a member's failure.
/// </summary>
[Trait("Category", "Session")]
public sealed class SessionTaskSetTests
{
    static readonly TimeSpan s_bound = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task QuietSet_JoinsImmediately()
    {
        // Arrange
        var set = Create.TaskSet(new FakeTimeProvider());
        set.Add(Task.CompletedTask);

        // Act
        var quiet = await set.JoinAsync(s_bound);

        // Assert
        Assert.True(quiet);
    }

    [Fact]
    public async Task JoinAsync_WaitsForALiveTask()
    {
        // Arrange
        var gate = Create.Gate();
        var set = Create.TaskSet(new FakeTimeProvider());
        set.Add(gate.Task);
        var join = set.JoinAsync(s_bound);

        // Act
        gate.SetResult();
        var quiet = await join;

        // Assert
        Assert.True(quiet);
    }

    [Fact]
    public async Task JoinAsync_GivesUpAtTheBound()
    {
        // Arrange
        // A task nothing will complete.
        var time = new FakeTimeProvider();
        var set = Create.TaskSet(time);
        set.Add(Create.Gate().Task);
        var join = set.JoinAsync(s_bound);

        // Act
        time.Advance(s_bound + TimeSpan.FromSeconds(1));
        var quiet = await join;

        // Assert
        Assert.False(quiet);
    }

    [Fact]
    public async Task TaskAddedDuringAJoin_IsJoinedToo()
    {
        // Arrange
        var first = Create.Gate();
        var second = Create.Gate();
        var set = Create.TaskSet(new FakeTimeProvider());
        set.Add(first.Task);
        var join = set.JoinAsync(s_bound);
        set.Add(second.Task);

        // Act
        // Completing only the first must not end the join.
        first.SetResult();
        await Task.Yield();
        Assert.False(join.IsCompleted);
        second.SetResult();
        var quiet = await join;

        // Assert
        Assert.True(quiet);
    }

    [Fact]
    public async Task FaultingMember_DoesNotFaultTheJoin()
    {
        // Arrange
        var gate = Create.Gate();
        var set = Create.TaskSet(new FakeTimeProvider());
        set.Add(FaultAfterAsync(gate.Task));
        var join = set.JoinAsync(s_bound);

        // Act
        gate.SetResult();
        var quiet = await join;

        // Assert
        // The member's failure is absorbed, so the join reports a quiet set rather
        // than surfacing an exception the caller cannot act on.
        Assert.True(quiet);

        static async Task FaultAfterAsync(Task released)
        {
            await released;
            throw new InvalidOperationException("member broke");
        }
    }
}
