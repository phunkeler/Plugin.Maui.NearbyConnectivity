namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Public-surface promises owned by this folder's types.
/// </summary>
[Trait("Category", "Session")]
public sealed class BindingContractTests
{
    // -------------------------------------------------------------------------
    // Count is bindable
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeviceCollection_RaisesPropertyChangedForCount()
    {
        // {Binding Devices.Count} rendered once and never updated, so a consumer wrote a separate
        // counting service over the same stream.

        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        using var devices = Create.Devices(session);
        var raised = new List<string?>();
        devices.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await platform.EmitDeviceFoundAsync(Create.Device("peer-1"));
        await Wait.UntilAsync(() => devices.Count == 1);

        // Assert
        Assert.Contains(nameof(devices.Count), raised);
    }

    // -------------------------------------------------------------------------
    // the bindable status object
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Status_SeedsFromTheSessionAndTracksActivity()
    {
        // Arrange
        var platform = new FakeNearby { Availability = NearbyAvailability.Available };
        await using var session = Create.Session(platform);
        using var status = new NearbySessionState(session, new InlineDispatcher());
        await Wait.UntilAsync(() => status.IsReady);

        // Act
        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        await Wait.UntilAsync(() => status.IsAdvertising);
        Assert.True(status.IsAdvertising);
        Assert.False(status.IsDiscovering);
    }

    [Fact]
    public async Task Status_OnTheDispatchersThread_SeedsBeforeTheConstructorReturns()
    {
        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

        // Act
        using var status = new NearbySessionState(session, new QueuingDispatcher());

        // Assert
        Assert.True(status.IsDiscovering);
    }

    [Fact]
    public async Task Status_SurfacesAFaultThatEndedAnActivity()
    {
        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        var token = TestContext.Current.CancellationToken;
        using var status = new NearbySessionState(session, new InlineDispatcher());
        await session.Discoverer.StartAsync(token);
        await Wait.UntilAsync(() => status.IsDiscovering);

        // Act
        platform.FaultDiscoverStream(new InvalidOperationException("radio dropped"));

        // Assert
        await Wait.UntilAsync(() => status.DiscoveryStop?.Reason is NearbyActivityStopReason.Faulted);
        Assert.False(status.IsDiscovering);
        Assert.NotNull(status.DiscoveryStop?.Error);
    }

    [Fact]
    public async Task Status_AttributesAStopToItsOwnActivity()
    {
        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        var token = TestContext.Current.CancellationToken;
        using var status = new NearbySessionState(session, new InlineDispatcher());
        await session.Advertiser.StartAsync(token);
        await session.Discoverer.StartAsync(token);
        await Wait.UntilAsync(() => status.IsAdvertising && status.IsDiscovering);

        // Act
        platform.FaultDiscoverStream(new InvalidOperationException("radio dropped"));

        // Assert
        await Wait.UntilAsync(() => status.DiscoveryStop is not null);
        Assert.Equal(NearbyActivityStopReason.Faulted, status.DiscoveryStop!.Reason);
        Assert.Null(status.AdvertisingStop);
        Assert.True(status.IsAdvertising);
    }

    [Fact]
    public async Task Status_RestartingAnActivity_ClearsItsStop()
    {
        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        var token = TestContext.Current.CancellationToken;

        using var status = new NearbySessionState(session, new InlineDispatcher());

        await session.Discoverer.StartAsync(token);
        await session.Discoverer.StopAsync();
        await Wait.UntilAsync(() => status.DiscoveryStop?.Reason is NearbyActivityStopReason.Requested);

        // Act
        await session.Discoverer.StartAsync(token);

        // Assert
        await Wait.UntilAsync(() => status.IsDiscovering);

        Assert.Null(status.DiscoveryStop);
    }

    [Fact]
    public async Task Status_Dispose_DropsAChangeAlreadyQueued()
    {
        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);

        var dispatcher = new QueuingDispatcher { IsDispatchRequired = true };
        var status = new NearbySessionState(session, dispatcher);
        await Wait.UntilAsync(() => dispatcher.Queued > 0);
        dispatcher.RunQueued();

        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        await Wait.UntilAsync(() => dispatcher.Queued > 0);

        // Act
        status.Dispose();
        dispatcher.RunQueued();

        // Assert
        Assert.False(status.IsAdvertising);
    }

    [Fact]
    public async Task Status_NoDispatcher_Throws()
    {
        // Same contract as the device collection: with no dispatcher supplied and none on this
        // thread, say so rather than raise PropertyChanged where nothing renders.

        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => new NearbySessionState(session));
    }

    [Fact]
    public async Task Status_StopsWatchingWhenDisposed()
    {
        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);

        var status = new NearbySessionState(session, new InlineDispatcher());
        await Wait.UntilAsync(() => status.IsReady);

        // Act
        status.Dispose();
        status.Dispose();

        await session.Advertiser.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        // The disposed object stopped tracking rather than throwing.
        Assert.False(status.IsAdvertising);
    }

    [Fact]
    public async Task BothBindableTypes_DisposedMidPark_DoNotFaultTheDispatcher()
    {
        // Teardown is not a fault: disposing either type while the session keeps raising must
        // unwind quietly, not arrive on the bound thread as an exception a consumer cannot catch.

        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        var token = TestContext.Current.CancellationToken;

        var dispatcher = new InlineDispatcher();
        var devices = Create.Devices(session, dispatcher);
        var status = new NearbySessionState(session, dispatcher);

        await session.Discoverer.StartAsync(token);
        await Wait.UntilAsync(() => status.IsDiscovering);

        // Act
        devices.Dispose();
        status.Dispose();

        await platform.EmitDeviceFoundAsync(Create.Device("peer-after-dispose"));
        await Task.Delay(50, token);

        // Assert
        Assert.Null(dispatcher.Fault);
        Assert.Empty(devices);
    }
}
