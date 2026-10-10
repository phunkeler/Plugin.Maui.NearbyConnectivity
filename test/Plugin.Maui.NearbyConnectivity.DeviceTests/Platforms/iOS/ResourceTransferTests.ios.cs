namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// Inbound resource (file) transfer callbacks: <c>OnResourceStarted</c> wires real KVO progress
/// through to <see cref="NearbyConnection.InboundTransfersAsync(CancellationToken)"/>, and <c>OnResourceFinished</c> copies
/// the received file into the library's staging directory and routes a
/// <see cref="NearbyFilePayload"/>.
/// </summary>
[Collection(StagingTests.Name)]
public sealed class ResourceTransferTests : DeviceTest
{
    [Fact]
    public async Task ResourceFinished_CopiesFileAndRoutesFilePayload()
    {
        // Arrange
        // A live connection and a real source file where MPC would have staged it.
        await using var platform = Create.NearbyTransport(new NearbyOptions { ServiceId = "devtest" });
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (connection, _) = await Create.ConnectedAsync(platform, peerID, cts.Token);

        byte[] expected = [10, 20, 30];
        var sourcePath = Path.Combine(Path.GetTempPath(), $"devtest-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(sourcePath, expected, cts.Token);

        // Act
        using var sourceUrl = NSUrl.FromFilename(sourcePath);
        platform.Ios().OnResourceFinished("photo.bin", peerID, sourceUrl, error: null);

        var received = await Receive.FirstAsync(connection, cts.Token);

        // Assert
        // Payload routed, content moved into staging, MPC's own file consumed.
        var filePayload = Assert.IsType<NearbyFilePayload>(received);
        Assert.Equal(expected, await File.ReadAllBytesAsync(filePayload.File.FullPath, cts.Token));
        Assert.StartsWith(InboundStaging.Root, filePayload.File.FullPath, StringComparison.Ordinal);
        Assert.False(File.Exists(sourcePath));
    }

    [Fact]
    public async Task ResourceFinishedWithError_EndsInboundTransfersWithFailed()
    {
        // Arrange
        // A started resource, so the finish has a transfer id to report against.
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (connection, _) = await Create.ConnectedAsync(platform, peerID, cts.Token);
        var transfers = connection.InboundTransfersAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        using var progress = NSProgress.FromTotalUnitCount(100);
        platform.Ios().OnResourceStarted("photo.bin", peerID, progress);

        // Act
        using var error = new NSError((NSString)"devtest", code: 42);
        platform.Ios().OnResourceFinished("photo.bin", peerID, localUrl: null, error);

        // Assert
        // Skip any InProgress the observer reported on subscription; the finish is last.
        NearbyTransferProgress last;
        do
        {
            Assert.True(await transfers.MoveNextAsync());
            last = transfers.Current;
        }
        while (last.Status is NearbyTransferStatus.InProgress);

        Assert.Equal(NearbyTransferStatus.Failed, last.Status);
        await transfers.DisposeAsync();
    }

    [Fact]
    public async Task CancelInboundTransfer_CancelsTheReceiversProgress()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (connection, _) = await Create.ConnectedAsync(platform, peerID, cts.Token);
        var transfers = connection.InboundTransfersAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        using var progress = NSProgress.FromTotalUnitCount(100);
        platform.Ios().OnResourceStarted("photo.bin", peerID, progress);
        progress.CompletedUnitCount = 50;
        Assert.True(await transfers.MoveNextAsync());

        // Act
        await connection.CancelInboundTransferAsync(transfers.Current.PayloadId);

        // Assert
        Assert.True(progress.Cancelled);
        Assert.True(connection.IsOpen);
        await transfers.DisposeAsync();
    }

    [Fact]
    public async Task ResourceFinishedWithError_AfterACancel_EndsInboundTransfersWithCanceled()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (connection, _) = await Create.ConnectedAsync(platform, peerID, cts.Token);
        var transfers = connection.InboundTransfersAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        using var progress = NSProgress.FromTotalUnitCount(100);
        platform.Ios().OnResourceStarted("photo.bin", peerID, progress);
        progress.CompletedUnitCount = 50;
        Assert.True(await transfers.MoveNextAsync());
        await connection.CancelInboundTransferAsync(transfers.Current.PayloadId);

        // Act
        using var error = new NSError((NSString)"devtest", code: 42);
        platform.Ios().OnResourceFinished("photo.bin", peerID, localUrl: null, error);

        // Assert
        NearbyTransferProgress last;
        do
        {
            Assert.True(await transfers.MoveNextAsync());
            last = transfers.Current;
        }
        while (last.Status is NearbyTransferStatus.InProgress);

        Assert.Equal(NearbyTransferStatus.Canceled, last.Status);
        Assert.Equal(50, last.BytesTransferred);
        await transfers.DisposeAsync();
    }

    [Fact]
    public async Task ResourceFinishedWithoutError_AfterACancel_StillCompletes()
    {
        // Arrange
        await using var platform = Create.NearbyTransport(new NearbyOptions { ServiceId = "devtest" });
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (connection, _) = await Create.ConnectedAsync(platform, peerID, cts.Token);
        var transfers = connection.InboundTransfersAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        using var progress = NSProgress.FromTotalUnitCount(3);
        platform.Ios().OnResourceStarted("photo.bin", peerID, progress);
        progress.CompletedUnitCount = 1;
        Assert.True(await transfers.MoveNextAsync());
        await connection.CancelInboundTransferAsync(transfers.Current.PayloadId);

        var sourcePath = Path.Combine(Path.GetTempPath(), $"devtest-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3], cts.Token);

        // Act
        using var sourceUrl = NSUrl.FromFilename(sourcePath);
        platform.Ios().OnResourceFinished("photo.bin", peerID, sourceUrl, error: null);

        // Assert
        NearbyTransferProgress last;
        do
        {
            Assert.True(await transfers.MoveNextAsync());
            last = transfers.Current;
        }
        while (last.Status is NearbyTransferStatus.InProgress);

        Assert.Equal(NearbyTransferStatus.Completed, last.Status);
        Assert.IsType<NearbyFilePayload>(await Receive.FirstAsync(connection, cts.Token)).Dispose();
        await transfers.DisposeAsync();
    }

    [Fact]
    public async Task ResourceFinishedWithError_RoutesNothing()
    {
        // Arrange
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (connection, _) = await Create.ConnectedAsync(platform, peerID, cts.Token);

        // Act
        using var error = new NSError((NSString)"devtest", code: 42);
        platform.Ios().OnResourceFinished("photo.bin", peerID, localUrl: null, error);

        // Assert
        await Receive.AssertNothingReceivedAsync(connection);
    }

    [Fact]
    public async Task ResourceStarted_RealKvoProgressReachesInboundTransfers()
    {
        // Arrange
        // A live connection with an inbound-progress observer attached.
        await using var platform = Create.NearbyTransport();
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (connection, _) = await Create.ConnectedAsync(platform, peerID, cts.Token);
        var transfers = connection.InboundTransfersAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        using var progress = NSProgress.FromTotalUnitCount(100);

        // Act
        // The real KVO registration fires when the native progress advances.
        platform.Ios().OnResourceStarted("photo.bin", peerID, progress);
        progress.CompletedUnitCount = 50;

        // Assert
        Assert.True(await transfers.MoveNextAsync());
        var update = transfers.Current;

        Assert.Equal(NearbyTransferStatus.InProgress, update.Status);
        Assert.Equal(100, update.TotalBytes);
        Assert.Equal(50, update.BytesTransferred);
        await transfers.DisposeAsync();
    }

    [Fact]
    public async Task TwoResourcesWithTheSameName_BothSurvive()
    {
        // Arrange
        // The collision the reservation exists for: one peer sends photo.bin twice.
        await using var platform = Create.NearbyTransport(new NearbyOptions { ServiceId = "devtest" });
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (connection, _) = await Create.ConnectedAsync(platform, peerID, cts.Token);

        byte[] first = [1, 1, 1];
        byte[] second = [2, 2, 2];
        var paths = new List<string>();

        // Act
        // Both resources arrive under the same name, then both are read through the one
        // enumerator the receive stream allows.
        foreach (var content in new[] { first, second })
        {
            var sourcePath = Path.Combine(Path.GetTempPath(), $"devtest-{Guid.NewGuid():N}.bin");
            await File.WriteAllBytesAsync(sourcePath, content, cts.Token);

            using var sourceUrl = NSUrl.FromFilename(sourcePath);
            platform.Ios().OnResourceFinished("photo.bin", peerID, sourceUrl, error: null);
        }

        foreach (var received in await Receive.TakeAsync(connection, 2, cts.Token))
        {
            paths.Add(Assert.IsType<NearbyFilePayload>(received).File.FullPath);
        }

        // Assert
        // Distinct destinations, neither clobbered.
        Assert.NotEqual(paths[0], paths[1]);
        Assert.Equal(first, await File.ReadAllBytesAsync(paths[0], cts.Token));
        Assert.Equal(second, await File.ReadAllBytesAsync(paths[1], cts.Token));
    }

    [Fact]
    public async Task Dispose_RemovesAStagedFileNobodyMoved()
    {
        // Arrange
        // A delivered file the consumer never moved out of staging.
        var platform = Create.NearbyTransport(new NearbyOptions { ServiceId = "devtest" });
        using var peerID = Create.PeerId("Alice");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (connection, _) = await Create.ConnectedAsync(platform, peerID, cts.Token);

        var sourcePath = Path.Combine(Path.GetTempPath(), $"devtest-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(sourcePath, [7, 7, 7], cts.Token);

        using var sourceUrl = NSUrl.FromFilename(sourcePath);
        platform.Ios().OnResourceFinished("photo.bin", peerID, sourceUrl, error: null);

        var received = await Receive.FirstAsync(connection, cts.Token);
        var stagedPath = Assert.IsType<NearbyFilePayload>(received).File.FullPath;

        // Act
        await platform.DisposeAsync();

        // Assert
        Assert.False(File.Exists(stagedPath));
    }
}
