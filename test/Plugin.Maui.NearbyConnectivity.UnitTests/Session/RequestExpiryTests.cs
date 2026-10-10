using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Covers <see cref="NearbyConnectionRequest.Expired"/> and
/// <see cref="NearbyConnectionRequest.ExpiresIn"/> — the deadline in the two shapes a user
/// interface needs it: a task a prompt can be raced against, and a remaining duration a countdown
/// can render.
/// </summary>
[Trait("Category", "Session")]
public sealed class RequestExpiryTests
{
    [Fact]
    public void MarkExpired_IsIdempotent()
    {
        // The expiry timer and a teardown can both reach it.

        // Arrange
        var request = Create.Request();

        // Act
        request.MarkExpired();
        request.MarkExpired();

        // Assert
        Assert.True(request.Expired.IsCompleted);
        Assert.False(request.IsPending);
    }

    [Fact]
    public void MarkExpired_AfterAnswered_LeavesExpiredIncomplete()
    {
        // The order an accept-then-expiry race produces. The claim settles who wins, but both
        // paths can still reach the request object, and a request answered in time never expires.

        // Arrange
        var request = Create.Request();
        request.MarkAnswered();

        // Act
        request.MarkExpired();

        // Assert
        Assert.False(request.Expired.IsCompleted);
        Assert.False(request.IsPending);
    }

    [Fact]
    public async Task AnExpiredRequest_CompletesExpiredThroughTheSession()
    {
        // The path that matters most, driven by the real expiry timer rather than by calling
        // MarkExpired directly.

        // Arrange
        var time = new FakeTimeProvider();
        var platform = new FakeNearby();
        await using var session = Create.Session(platform, timeProvider: time);
        var token = TestContext.Current.CancellationToken;

        await session.Advertiser.StartAsync(token);

        var device = Create.Device("asker");
        await platform.EmitRequestAsync(
            device,
            () => Create.Connection(device),
            deadline: time.GetUtcNow() + TimeSpan.FromSeconds(30));

        var request = await Take.RequestAsync(session, token);
        Assert.False(request.Expired.IsCompleted);

        // Act
        time.Advance(TimeSpan.FromSeconds(31));

        // Assert
        await Wait.UntilAsync(() => request.Expired.IsCompleted);

        Assert.False(request.IsPending);
    }

    [Fact]
    public void ExpiresIn_CountsDownAndFloorsAtZero()
    {
        // What a countdown binds. Never negative, so a view can render it without a guard.

        // Arrange
        var future = Create.Request(deadline: DateTimeOffset.UtcNow.AddMinutes(1));
        var past = Create.Request(deadline: DateTimeOffset.UtcNow.AddMinutes(-1));

        // Assert
        Assert.InRange(future.ExpiresIn, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.Zero, past.ExpiresIn);
    }
}
