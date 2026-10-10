using System.Threading.Channels;

namespace Plugin.Maui.NearbyConnectivity.DeviceTests;

/// <summary>
/// A discovery run held open on the transport, as the session holds one while it discovers.
/// </summary>
/// <remarks>
/// The transport counts runs toward its idle span. A test that drives the adapter directly holds
/// no run, so the last connection to end releases the span and forgets every peer in it. A test
/// that asserts a peer outlives its connection holds one of these.
/// </remarks>
sealed class DiscoveryRun : IAsyncDisposable
{
    readonly CancellationTokenSource _cts = new();
    readonly Channel<NearbyDeviceEvent> _events = Channel.CreateUnbounded<NearbyDeviceEvent>();
    Task _pump = Task.CompletedTask;

    /// <summary>The events the run reported, in order.</summary>
    internal ChannelReader<NearbyDeviceEvent> Events => _events.Reader;

    /// <summary>Starts discovery through the transport and waits for the platform start to finish.</summary>
    /// <param name="platform">The transport to discover through.</param>
    /// <param name="cancellationToken">Token to stop waiting for the start.</param>
    /// <returns>The running discovery. Dispose it to stop.</returns>
    internal static async Task<DiscoveryRun> StartAsync(NearbyTransport platform, CancellationToken cancellationToken)
    {
        var run = new DiscoveryRun();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        run._pump = run.PumpAsync(platform, started);
        await started.Task.WaitAsync(cancellationToken);

        return run;
    }

    async Task PumpAsync(NearbyTransport platform, TaskCompletionSource started)
    {
        try
        {
            await foreach (var item in platform.DiscoverAsync(started, _cts.Token))
            {
                _events.Writer.TryWrite(item);
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
        finally
        {
            _events.Writer.TryComplete();
        }
    }

    /// <summary>Stops the run and waits for the transport to end it.</summary>
    /// <returns>A task that completes when the run has ended.</returns>
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        await _pump;
        _cts.Dispose();
    }
}
