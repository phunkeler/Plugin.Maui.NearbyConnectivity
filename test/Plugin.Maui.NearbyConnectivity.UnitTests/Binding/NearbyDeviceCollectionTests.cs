using System.Collections.Specialized;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Tests for <see cref="NearbyDeviceCollection"/>, the optional bindable projection over
/// <see cref="INearbyConnectivity.DeviceChanged"/>.
/// </summary>
[Trait("Category", "Devices")]
public class NearbyDeviceCollectionTests
{
    public sealed class Construction : NearbyDeviceCollectionTests
    {
        [Fact]
        public void NullSession_Throws()
            => Assert.Throws<ArgumentNullException>(
                () => new NearbyDeviceCollection(null!, new InlineDispatcher()));

        // On net10.0 there is no dispatcher to find, and off the UI thread there is none either.
        // Throwing says so, rather than binding to a thread nothing renders on.
        [Fact]
        public void NoDispatcher_Throws()
            => Assert.Throws<InvalidOperationException>(
                () => new NearbyDeviceCollection(Create.Session(new FakeNearby())));

        // Constructing mid-session must show what is already there: the event carries deltas, not
        // history, so a collection built after discovery started would otherwise be
        // empty until the next change.
        [Fact]
        public async Task Seeds_FromDevicesAlreadyKnown()
        {
            // Arrange
            var platform = new FakeNearby();
            var session = Create.Session(platform);
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);
            await platform.EmitDeviceFoundAsync(Create.Device("a", "Alice"));

            // Act
            using var devices = Create.Devices(session);

            // Assert
            Assert.Single(devices);
            Assert.Equal("a", devices[0].Id);
        }

        // Dispatch queues even from the dispatcher's own thread, so a page constructing this there
        // would bind an empty list and fill it a frame later.
        [Fact]
        public async Task OnTheDispatchersThread_SeedsBeforeTheConstructorReturns()
        {
            // Arrange
            var platform = new FakeNearby();
            var session = Create.Session(platform);
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);
            await platform.EmitDeviceFoundAsync(Create.Device("a", "Alice"));

            // Act
            using var devices = Create.Devices(session, new QueuingDispatcher());

            // Assert
            Assert.Single(devices);
        }
    }

    public sealed class Projection : NearbyDeviceCollectionTests
    {
        [Fact]
        public async Task DiscoveredDevice_IsAdded()
        {
            // Arrange
            var platform = new FakeNearby();
            var session = Create.Session(platform);
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

            using var devices = Create.Devices(session);

            // Act
            await platform.EmitDeviceFoundAsync(Create.Device("a", "Alice"));
            await Wait.UntilAsync(() => devices.Count == 1);

            // Assert
            Assert.Single(devices);
        }

        // A device is a value, so an update is a replacement. The indexer assignment raises Replace,
        // which is what lets a bound row update in place instead of moving.
        [Fact]
        public async Task StatusChange_ReplacesInPlace_AndRaisesReplace()
        {
            // Arrange
            var platform = new FakeNearby();
            var session = Create.Session(platform);
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

            var device = Create.Device("a", "Alice");
            device = await platform.EmitDeviceFoundAsync(device);

            using var devices = Create.Devices(session);

            var actions = new List<NotifyCollectionChangedAction>();
            devices.CollectionChanged += (_, e) => actions.Add(e.Action);

            platform.ConnectResult = Create.Connection(device: device);

            // Act
            await device.ConnectAsync(TestContext.Current.CancellationToken);
            await Wait.UntilAsync(() => devices.Count == 1 && devices[0].Status is NearbyDeviceStatus.Connected);

            // Assert
            Assert.Single(devices);
            Assert.Equal(NearbyDeviceStatus.Connected, devices[0].Status);
            Assert.Contains(NotifyCollectionChangedAction.Replace, actions);
            Assert.DoesNotContain(NotifyCollectionChangedAction.Remove, actions);
        }

        [Fact]
        public async Task Mutations_GoThroughTheDispatcher()
        {
            // Arrange
            var platform = new FakeNearby();
            var session = Create.Session(platform);
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

            var dispatcher = new InlineDispatcher();
            using var devices = Create.Devices(session, dispatcher);

            // Act
            await platform.EmitDeviceFoundAsync(Create.Device("a", "Alice"));
            await Wait.UntilAsync(() => devices.Count == 1);

            // Assert
            Assert.True(dispatcher.Count > 0, "Every mutation must run on the caller's dispatcher.");
        }

        [Fact]
        public async Task Dispose_StopsTracking()
        {
            // Arrange
            var platform = new FakeNearby();
            var session = Create.Session(platform);
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

            var devices = Create.Devices(session);
            devices.Dispose();

            // Act
            await platform.EmitDeviceFoundAsync(Create.Device("a", "Alice"));

            // A fixed wait, not a poll: this asserts that something never arrives, and polling can
            // only establish that it has not arrived yet.
            await Task.Delay(50, TestContext.Current.CancellationToken);

            // Assert
            // A disposed collection must stop applying changes.
            Assert.Empty(devices);
        }

        [Fact]
        public async Task Dispose_DropsAChangeAlreadyQueued()
        {
            // Arrange
            var platform = new FakeNearby();
            var session = Create.Session(platform);
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

            var dispatcher = new QueuingDispatcher { IsDispatchRequired = true };
            var devices = Create.Devices(session, dispatcher);

            await platform.EmitDeviceFoundAsync(Create.Device("a", "Alice"));
            await Wait.UntilAsync(() => dispatcher.Queued > 0);

            // Act
            devices.Dispose();
            dispatcher.RunQueued();

            // Assert
            Assert.Empty(devices);
        }

        [Fact]
        public void Dispose_IsIdempotent()
        {
            // Arrange
            var devices = Create.Devices(Create.Session(new FakeNearby()));

            // Act
            devices.Dispose();
            devices.Dispose();

            // Assert
            Assert.Empty(devices);
        }
    }

    public sealed class Filtering : NearbyDeviceCollectionTests
    {
        [Fact]
        public async Task NonMatchingDevice_IsNotShown()
        {
            // Arrange
            var platform = new FakeNearby();
            var session = Create.Session(platform);
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

            using var devices = Create.Devices(
                session, filter: static d => d.Status is NearbyDeviceStatus.Connected);

            // Act
            await platform.EmitDeviceFoundAsync(Create.Device("a", "Alice"));
            await Task.Delay(50, TestContext.Current.CancellationToken);

            // Assert
            // A device that fails the filter must never enter the collection.
            Assert.Empty(devices);
        }

        // The filter is re-evaluated per change, so leaving the filtered set is a removal even
        // though the session reported an update.
        [Fact]
        public async Task DeviceLeavingTheFilteredSet_IsRemoved()
        {
            // Arrange
            var platform = new FakeNearby();
            var session = Create.Session(platform);
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

            var device = Create.Device("a", "Alice");
            device = await platform.EmitDeviceFoundAsync(device);

            using var devices = Create.Devices(
                session, filter: static d => d.Status is NearbyDeviceStatus.Visible);

            platform.ConnectResult = Create.Connection(device: device);

            // Act
            await device.ConnectAsync(TestContext.Current.CancellationToken);
            await Wait.UntilAsync(() => devices.Count == 0);

            // Assert
            Assert.Empty(devices);
        }
    }

    public sealed class Replacement : NearbyDeviceCollectionTests
    {
        // A device IS an immutable snapshot, so keeping the old instance would leave a stale one
        // bound forever. Replacement is the update.
        [Fact]
        public async Task ChangedDevice_ReplacesTheEntry()
        {
            // Arrange
            var platform = new FakeNearby();
            var session = Create.Session(platform);
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

            var device = Create.Device("a", "Alice");
            device = await platform.EmitDeviceFoundAsync(device);

            using var rows = Create.Devices(session);

            var original = rows[0];
            platform.ConnectResult = Create.Connection(device: device);

            // Act
            await device.ConnectAsync(TestContext.Current.CancellationToken);
            await Wait.UntilAsync(() => rows[0].Status is NearbyDeviceStatus.Connected);

            // Assert
            Assert.NotSame(original, rows[0]);
            Assert.Single(rows);
        }
    }

    public sealed class Faults : NearbyDeviceCollectionTests
    {
        // The filter is caller code. Its exception is the fault as thrown, and it must not escape
        // onto the bound thread.
        [Fact]
        public async Task ThrowingFilter_IsTheFault()
        {
            // Arrange
            var platform = new FakeNearby();
            var session = Create.Session(platform);
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

            var thrown = new InvalidOperationException("filter broke");
            var dispatcher = new InlineDispatcher();
            using var devices = Create.Devices(session, dispatcher, filter: _ => throw thrown);
            var raised = new List<string?>();
            devices.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            // Act
            await platform.EmitDeviceFoundAsync(Create.Device("a", "Alice"));
            await Wait.UntilAsync(() => devices.IsFaulted);

            // Assert
            Assert.Same(thrown, devices.Exception);
            Assert.Null(dispatcher.Fault);
            Assert.Contains(nameof(devices.IsFaulted), raised);
        }

        // A handler's exception is the handler's bug. Blaming the collection would freeze the
        // list with nothing on screen, so it surfaces on the dispatcher as it would from any
        // ObservableCollection.
        [Fact]
        public async Task ThrowingCollectionChangedHandler_IsNotTheCollectionsFault()
        {
            // Arrange
            var platform = new FakeNearby();
            var session = Create.Session(platform);
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

            var thrown = new InvalidOperationException("handler broke");
            var dispatcher = new InlineDispatcher();
            using var devices = Create.Devices(session, dispatcher);
            devices.CollectionChanged += (_, _) => throw thrown;

            // Act
            await platform.EmitDeviceFoundAsync(Create.Device("a", "Alice"));
            await Wait.UntilAsync(() => dispatcher.Fault is not null);

            // Assert
            Assert.Same(thrown, dispatcher.Fault);
            Assert.False(devices.IsFaulted);
        }

        [Fact]
        public async Task CollectionChanged_IsRaisedByTheCollection()
        {
            // Arrange
            var platform = new FakeNearby();
            var session = Create.Session(platform);
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

            using var devices = Create.Devices(session);
            object? sender = null;
            devices.CollectionChanged += (s, _) => sender = s;

            // Act
            await platform.EmitDeviceFoundAsync(Create.Device("a", "Alice"));
            await Wait.UntilAsync(() => sender is not null);

            // Assert
            Assert.Same(devices, sender);
        }

        // On the dispatcher's own thread the seed runs inside the constructor. A throwing filter
        // there must still surface as itself.
        [Fact]
        public async Task ThrowingFilter_OnTheDispatchersThread_IsTheFault()
        {
            // Arrange
            var platform = new FakeNearby();
            var session = Create.Session(platform);
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);
            await platform.EmitDeviceFoundAsync(Create.Device("a", "Alice"));

            var thrown = new InvalidOperationException("filter broke");

            // Act
            using var devices = Create.Devices(session, new QueuingDispatcher(), filter: _ => throw thrown);

            // Assert
            Assert.Same(thrown, devices.Exception);
        }

        // Disposal is a normal teardown. It must not reach the fault path.
        [Fact]
        public async Task Disposal_DoesNotRethrow()
        {
            // Arrange
            var platform = new FakeNearby();
            var session = Create.Session(platform);
            await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);

            var dispatcher = new InlineDispatcher();
            var devices = Create.Devices(session, dispatcher);

            // Act
            devices.Dispose();
            await platform.EmitDeviceFoundAsync(Create.Device("a", "Alice"));

            // Assert
            Assert.Null(dispatcher.Fault);
            Assert.False(devices.IsFaulted);
        }
    }
}
