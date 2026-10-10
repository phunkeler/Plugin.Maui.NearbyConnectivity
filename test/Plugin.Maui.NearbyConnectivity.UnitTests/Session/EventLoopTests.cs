namespace Plugin.Maui.NearbyConnectivity.UnitTests;

[Trait("Category", "Session")]
public sealed class EventLoopTests
{
    [Fact]
    public async Task DeviceChanged_IsRaisedAfterDevicesHoldsTheChange()
    {
        // Arrange
        var platform = new FakeNearby();
        var session = Create.Session(platform);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);
        var seen = new List<NearbyDeviceStatus?>();
        session.DeviceChanged += (_, change) =>
        {
            lock (seen)
            {
                seen.Add(session.StatusOf(change.Device.Id));
            }
        };

        // Act
        await platform.EmitDeviceFoundAsync(Create.Device("a"));
        await Wait.UntilAsync(() => { lock (seen) { return seen.Count == 1; } });

        // Assert
        Assert.Equal([NearbyDeviceStatus.Visible], seen);
    }

    [Fact]
    public async Task ThrowingHandler_IsLogged_AndTheOthersStillRun()
    {
        // Arrange
        var logger = new RecordingLogger();
        var loop = new EventLoop(logger);
        var ran = Create.Gate();
        EventHandler<int>? handlers = null;
        handlers += (_, _) => throw new InvalidOperationException("handler broke");
        handlers += (_, _) => ran.TrySetResult();

        // Act
        loop.Raise(handlers, this, 1);
        await ran.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(1600, logger.EventIds);
    }

    [Fact]
    public async Task EveryEvent_SharesOneOrder()
    {
        // Arrange
        var loop = new EventLoop(new RecordingLogger());
        var order = new List<string>();
        var done = Create.Gate();
        EventHandler<int> device = (_, n) => order.Add($"device-{n}");
        EventHandler state = (_, _) => order.Add("state");
        EventHandler last = (_, _) => done.TrySetResult();

        // Act
        loop.Raise(device, this, 1);
        loop.Raise(state, this);
        loop.Raise(device, this, 2);
        loop.Raise(last, this);
        await done.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["device-1", "state", "device-2"], order);
    }

    [Fact]
    public async Task HandlerSubscribedAfterARaise_DoesNotReceiveIt()
    {
        // Arrange
        var loop = new EventLoop(new RecordingLogger());
        var blocked = Create.Gate();
        var received = new List<int>();
        var done = Create.Gate();
        EventHandler<int>? handlers = (_, _) => blocked.Task.Wait(TestContext.Current.CancellationToken);
        loop.Raise(handlers, this, 1);

        // Act
        handlers += (_, n) => received.Add(n);
        loop.Raise(handlers, this, 2);
        loop.Raise((EventHandler)((_, _) => done.TrySetResult()), this);
        blocked.SetResult();
        await done.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([2], received);
    }

    [Fact]
    public async Task Backlog_PastTheThreshold_IsWarnedOnce()
    {
        // Arrange
        var logger = new RecordingLogger();
        var loop = new EventLoop(logger);
        var running = Create.Gate();
        var blocked = Create.Gate();
        var done = Create.Gate();
        loop.Raise((EventHandler)((_, _) =>
        {
            running.TrySetResult();
            blocked.Task.Wait(TestContext.Current.CancellationToken);
        }), this);
        await running.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Act
        for (var i = 0; i < EventLoop.BacklogWarningThreshold * 2; i++)
        {
            loop.Raise((EventHandler)((_, _) => { }), this);
        }

        loop.Raise((EventHandler)((_, _) => done.TrySetResult()), this);
        blocked.SetResult();
        await done.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(logger.EventIds, id => id == 1601);
    }

    [Fact]
    public async Task BlockedHandler_DoesNotBlockTheSession()
    {
        // Arrange
        var platform = new FakeNearby();
        var session = Create.Session(platform);
        var blocked = Create.Gate();
        session.DeviceChanged += (_, _) => blocked.Task.Wait(TestContext.Current.CancellationToken);
        var device = await Create.DiscoveredAsync(session, platform, Create.Device("peer-1"));
        platform.ConnectResult = Create.Connection(device);

        // Act
        var connection = await device.ConnectAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(connection);
        Assert.Equal(NearbyDeviceStatus.Connected, session.StatusOf("peer-1"));
        blocked.SetResult();
    }
}
