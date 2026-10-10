namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Tests for <see cref="Broadcast{T}"/>, the fan-out behind every broadcast stream in the library.
/// <see cref="WatcherRelease"/> covers the subscribe/release contract both modes share, asserted
/// directly rather than through <see cref="DeviceRegistry"/> because watcher release is not
/// observable from a consumer-visible surface: an abandoned watcher fails silently, by buffering
/// forever rather than by misbehaving. <see cref="Replay"/> covers the replay seam the snapshot
/// delegate adds — the grammar <c>replayed* live*</c>, each item exactly once per enumerator.
/// </summary>
public class BroadcastTests
{
    [Trait("Category", "Devices")]
    public sealed class WatcherRelease : BroadcastTests
    {
        [Fact]
        public async Task EnumeratorDisposedWithoutReading_ReleasesItsWatcher()
        {
            // Arrange
            var broadcast = new Broadcast<int>();
            var enumerator = broadcast.Stream(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

            // Act
            await enumerator.DisposeAsync();

            // Assert
            // The released watcher receives nothing, and its channel is completed.
            broadcast.Publish(1);
            Assert.False(await enumerator.MoveNextAsync());
        }

        [Fact]
        public async Task EnumeratorDisposedAfterReading_ReleasesItsWatcher()
        {
            // Arrange
            var broadcast = new Broadcast<int>();
            var enumerator = broadcast.Stream(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
            broadcast.Publish(1);
            await enumerator.MoveNextAsync();

            // Act
            await enumerator.DisposeAsync();

            // Assert
            broadcast.Publish(2);
            Assert.False(await enumerator.MoveNextAsync());
        }

        [Fact]
        public async Task DisposingTwice_ReleasesTheWatcherOnce()
        {
            // Arrange
            var broadcast = new Broadcast<int>();
            var enumerator = broadcast.Stream(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
            await enumerator.DisposeAsync();

            // Act
            await enumerator.DisposeAsync();

            // Assert
            // The second release is a no-op, not a second removal.
            broadcast.Publish(3);
            Assert.False(await enumerator.MoveNextAsync());
        }

        // One abandoned enumeration must not take its siblings' watchers with it.
        [Fact]
        public async Task DisposingOneEnumerator_LeavesTheOtherSubscribed()
        {
            // Arrange
            var broadcast = new Broadcast<int>();
            var abandoned = broadcast.Stream(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
            var kept = broadcast.Stream(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

            // Act
            await abandoned.DisposeAsync();

            // Assert
            // The survivor still receives, the abandoned one does not.
            broadcast.Publish(7);
            Assert.True(await kept.MoveNextAsync());
            Assert.Equal(7, kept.Current);
            Assert.False(await abandoned.MoveNextAsync());

            await kept.DisposeAsync();
        }

        // Eager subscribe is the other half of the contract, and the two are easy to break in
        // opposite directions: moving the unsubscribe out of the iterator must not move the
        // subscribe in with it.
        [Fact]
        public async Task ChangePublishedBeforeTheFirstRead_IsStillDelivered()
        {
            // Arrange
            var broadcast = new Broadcast<int>();
            var enumerator = broadcast.Stream(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

            // Act
            broadcast.Publish(42);

            // Assert
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(42, enumerator.Current);

            await enumerator.DisposeAsync();
        }

        // The no-snapshot mode is the state-delta shape: it must not replay anything, because the
        // current state is readable separately and history would be misleading.
        [Fact]
        public async Task WithoutASnapshot_NothingIsReplayed()
        {
            // Arrange
            var broadcast = new Broadcast<int>();
            broadcast.Publish(1);

            // Act
            var enumerator = broadcast.Stream(TestContext.Current.CancellationToken).GetAsyncEnumerator(Expire().Token);
            broadcast.Publish(2);

            // Assert
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(2, enumerator.Current);

            await enumerator.DisposeAsync();
        }
    }

    [Trait("Category", "Delivery")]
    public sealed class Replay : BroadcastTests
    {
        [Fact]
        public async Task Enumeration_ReplaysTheOutstandingSetFirst()
        {
            // Arrange
            var outstanding = new List<Item> { new("a"), new("b") };
            var broadcast = new Broadcast<Item>(() => outstanding);
            var received = new List<Item>();

            // Act
            await foreach (var item in broadcast.Replaying(TestContext.Current.CancellationToken).WithCancellation(Expire().Token))
            {
                received.Add(item);

                if (received.Count == 2)
                {
                    break;
                }
            }

            // Assert
            Assert.Equal(outstanding, received);
        }

        [Fact]
        public async Task LiveItems_FollowTheReplay()
        {
            // Arrange
            var broadcast = new Broadcast<Item>(() => [new Item("replayed")]);
            var live = new Item("live");
            var received = new List<Item>();
            var enumerator = broadcast.Replaying(TestContext.Current.CancellationToken).GetAsyncEnumerator(Expire().Token);

            // Act
            broadcast.Publish(live);

            while (await enumerator.MoveNextAsync())
            {
                received.Add(enumerator.Current);

                if (received.Count == 2)
                {
                    break;
                }
            }

            await enumerator.DisposeAsync();

            // Assert
            Assert.Equal("replayed", received[0].Name);
            Assert.Same(live, received[1]);
        }

        [Fact]
        public async Task ItemInTheHandoverWindow_IsYieldedExactlyOnce()
        {
            // The window: an item published between the subscribe and the snapshot read lands in
            // both. The snapshot delegate below publishes as it runs — the worst-case interleaving —
            // and the guard must suppress the duplicate: each item exactly once per enumerator.

            // Arrange
            var windowItem = new Item("window");
            Broadcast<Item> broadcast = null!;
            broadcast = new Broadcast<Item>(() =>
            {
                broadcast.Publish(windowItem);
                return [windowItem];
            });
            var after = new Item("after");
            var received = new List<Item>();
            var enumerator = broadcast.Replaying(TestContext.Current.CancellationToken).GetAsyncEnumerator(Expire().Token);

            // Act
            broadcast.Publish(after);

            while (await enumerator.MoveNextAsync())
            {
                received.Add(enumerator.Current);

                if (received.Count == 2)
                {
                    break;
                }
            }

            await enumerator.DisposeAsync();

            // Assert
            Assert.Same(windowItem, Assert.Single(received, i => i.Name == "window"));
            Assert.Same(after, received[1]);
        }

        [Fact]
        public async Task Enumerations_AreIndependent()
        {
            // Arrange
            var broadcast = new Broadcast<Item>(() => [new Item("replayed")]);
            var live = new Item("live");
            var first = broadcast.Replaying(TestContext.Current.CancellationToken).GetAsyncEnumerator(Expire().Token);
            var second = broadcast.Replaying(TestContext.Current.CancellationToken).GetAsyncEnumerator(Expire().Token);

            // Act
            broadcast.Publish(live);
            var firstReceived = await ReadAsync(first, 2);
            var secondReceived = await ReadAsync(second, 2);

            // Assert
            Assert.Same(live, firstReceived[1]);
            Assert.Same(live, secondReceived[1]);
        }

        static async Task<List<Item>> ReadAsync(IAsyncEnumerator<Item> enumerator, int count)
        {
            var received = new List<Item>();

            while (received.Count < count && await enumerator.MoveNextAsync())
            {
                received.Add(enumerator.Current);
            }

            await enumerator.DisposeAsync();
            return received;
        }
    }

    [Trait("Category", "Delivery")]
    public sealed class BoundToken : BroadcastTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task EitherToken_EndsTheEnumeration(bool cancelBound)
        {
            // Arrange
            using var bound = new CancellationTokenSource();
            using var perEnumeration = new CancellationTokenSource();
            var broadcast = new Broadcast<int>();
            var enumerator = broadcast.Stream(bound.Token).GetAsyncEnumerator(perEnumeration.Token);
            var read = enumerator.MoveNextAsync().AsTask();

            // Act
            await (cancelBound ? bound : perEnumeration).CancelAsync();

            // Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => read.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            await enumerator.DisposeAsync();
        }
    }

    sealed record Item(string Name);

    static CancellationTokenSource Expire() => new(TimeSpan.FromSeconds(5));
}
