using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Public-surface promises owned by this folder's types.
/// </summary>
[Trait("Category", "Session")]
public sealed class ConsentTests
{
    // -------------------------------------------------------------------------
    // one writer for consent
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetConsent_WithoutARule_ClearsTheRuleSetEarlier()
    {
        // One writer: the second call replaces both halves, so the accepting rule installed by the
        // first must not survive it. Observed through the outcome, the only surface that reports
        // the effective policy.

        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        var token = TestContext.Current.CancellationToken;

        var ruleRan = false;
        session.Advertiser.SetConsent(
            NearbyConsentPolicy.RejectAll,
            decide: (_, _) => { ruleRan = true; return ValueTask.FromResult<bool?>(true); });

        session.Advertiser.SetConsent(NearbyConsentPolicy.Hold);

        await session.Advertiser.StartAsync(token);
        await using var recorder = new ChangeRecorder(session);
        var device = Create.Device("peer-1");

        // Act
        await platform.EmitRequestAsync(device, () => Create.Connection(device));
        await recorder.WaitForAsync("peer-1", 1);

        // Assert
        // Hold holds the request rather than accepting it, and the cleared rule never ran.
        Assert.False(ruleRan);
        Assert.Equal(NearbyDeviceStatus.RequestReceived, session.StatusOf("peer-1"));
    }

    [Fact]
    public async Task DecideRule_SeesTheRequestersInfo()
    {
        // The consent predicate can decide on app-defined presence info.

        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        var info = "route:R17"u8.ToArray();
        byte[]? seen = null;

        session.Advertiser.SetConsent(
            NearbyConsentPolicy.Hold,
            decide: (d, _) =>
            {
                seen = d.Presence.Info.ToArray();
                return ValueTask.FromResult<bool?>(false);
            });

        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        var device = Create.Device("peer-1", "Alice", info);

        // Act
        await platform.EmitRequestAsync(device, () => Create.Connection(device));

        // Assert
        Assert.Equal(info, seen);
    }

    // -------------------------------------------------------------------------
    // the decide callback and the offer's deadline
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Decide_PastTheOffersDeadline_IsCancelled_AndTheRequestDeclined()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var platform = new FakeNearby();
        await using var session = Create.Session(platform, timeProvider: time);
        var token = TestContext.Current.CancellationToken;
        var deciding = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rejected = Create.Gate();

        session.Advertiser.SetConsent(
            NearbyConsentPolicy.Hold,
            decide: async (_, ct) =>
            {
                deciding.TrySetResult(ct);
                await Task.Delay(Timeout.Infinite, ct);
                return true;
            });

        await session.Advertiser.StartAsync(token);
        var device = Create.Device("peer-1");
        await platform.EmitRequestAsync(
            device,
            () => Create.Connection(device),
            onReject: () => rejected.TrySetResult(),
            deadline: time.GetUtcNow().AddSeconds(10));
        var decideToken = await deciding.Task.WaitAsync(token);

        // Act
        time.Advance(TimeSpan.FromSeconds(10));
        await rejected.Task.WaitAsync(token);

        // Assert
        Assert.True(decideToken.IsCancellationRequested);
        Assert.Null(session.StatusOf("peer-1"));
    }

    [Fact]
    public async Task Decide_WhenAdvertisingStops_IsCancelled()
    {
        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        var token = TestContext.Current.CancellationToken;
        var deciding = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);

        session.Advertiser.SetConsent(
            NearbyConsentPolicy.Hold,
            decide: async (_, ct) =>
            {
                deciding.TrySetResult(ct);
                await Task.Delay(Timeout.Infinite, ct);
                return true;
            });

        await session.Advertiser.StartAsync(token);
        var device = Create.Device("peer-1");
        await platform.EmitRequestAsync(device, () => Create.Connection(device));
        var decideToken = await deciding.Task.WaitAsync(token);

        // Act
        await session.Advertiser.StopAsync();

        // Assert
        Assert.True(decideToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Decide_AnsweringAfterTheDeadline_IsIgnored()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var platform = new FakeNearby();
        await using var session = Create.Session(platform, timeProvider: time);
        var token = TestContext.Current.CancellationToken;
        var answer = Create.Gate();
        var answered = Create.Gate();
        var accepted = false;
        var rejected = Create.Gate();

        session.Advertiser.SetConsent(
            NearbyConsentPolicy.Hold,
            decide: async (_, _) =>
            {
                await answer.Task;
                answered.TrySetResult();
                return true;
            });

        await session.Advertiser.StartAsync(token);
        var device = Create.Device("peer-1");
        await platform.EmitRequestAsync(
            device,
            () =>
            {
                accepted = true;
                return Create.Connection(device);
            },
            onReject: () => rejected.TrySetResult(),
            deadline: time.GetUtcNow().AddSeconds(10));

        // Act
        time.Advance(TimeSpan.FromSeconds(10));
        await rejected.Task.WaitAsync(token);
        answer.SetResult();
        await answered.Task.WaitAsync(token);
        await Task.Delay(50, token);

        // Assert
        Assert.False(accepted);
        Assert.Null(session.StatusOf("peer-1"));
    }
}
