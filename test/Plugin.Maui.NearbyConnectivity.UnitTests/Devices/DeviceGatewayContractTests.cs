using Microsoft.Extensions.Logging.Abstractions;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Public-surface promises owned by this folder's types.
/// </summary>
[Trait("Category", "Session")]
public sealed class DeviceGatewayContractTests
{
    // -------------------------------------------------------------------------
    // verbs resolve live from every source of a device, not just the registry
    // -------------------------------------------------------------------------

    [Fact]
    public void ADeviceMintedByThePlatformLayer_CarriesTheSessionsGateway()
    {
        // The promise: "the verbs resolve live, the data does not". It held only for devices read
        // from Devices — PeerLookup mints the device a connection and a request carry, and it had
        // no gateway, so Connection was null while connected and ConnectAsync threw
        // InvalidOperationException with a message claiming the opposite.
        //
        // Driven through PeerLookup rather than the session fake: the fake hands back devices the
        // test constructed, while on both real platforms the adapters mint them here
        // (AndroidAdapter.android.cs:155, IosAdapter.ios.cs:509).

        // Arrange
        var lookup = new PeerLookup { Logger = NullLogger.Instance };
        var gateway = new StubDeviceGateway();

        // Act
        lookup.Gateway = gateway;
        var device = lookup.Record("peer-1", "Alice");

        // Assert
        // The minted device resolves both verbs through the session rather than throwing
        // for want of a gateway.
        Assert.Null(device.Connection);
        Assert.Null(device.Request);
        Assert.Equal(1, gateway.TryGetConnectionCalls);
        Assert.Equal(1, gateway.TryGetRequestCalls);
    }

    // -------------------------------------------------------------------------
    // NearbyDevice.Request replaces the duplicate deadline
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ADeviceWithNoOutstandingRequest_HasNoRequest()
    {
        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);

        await session.Discoverer.StartAsync(TestContext.Current.CancellationToken);
        await platform.EmitDeviceFoundAsync(Create.Device("visible"));

        // Assert
        Assert.Null(session.Current("visible")?.Request);
    }

    /// <summary>Records that a device reached the gateway rather than throwing for want of one.</summary>
    sealed class StubDeviceGateway : INearbyDeviceGateway
    {
        public int TryGetConnectionCalls { get; private set; }

        public int TryGetRequestCalls { get; private set; }

        public Task<NearbyConnection> ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DisconnectAsync(string deviceId)
            => throw new NotSupportedException();

        public Task<NearbyConnection> AcceptRequestAsync(
            NearbyConnectionRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeclineRequestAsync(
            NearbyConnectionRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public bool TryGetConnection(string deviceId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out NearbyConnection? connection)
        {
            TryGetConnectionCalls++;
            connection = null;
            return false;
        }

        public bool TryGetRequest(string deviceId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out NearbyConnectionRequest? request)
        {
            TryGetRequestCalls++;
            request = null;
            return false;
        }

        public bool TryGetConnectDeadline(string deviceId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DateTimeOffset? deadline)
        {
            deadline = null;
            return false;
        }
    }
}
