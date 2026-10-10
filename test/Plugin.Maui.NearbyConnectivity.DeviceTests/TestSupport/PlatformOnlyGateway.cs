using System.Diagnostics.CodeAnalysis;

namespace Plugin.Maui.NearbyConnectivity.DeviceTests;

/// <summary>
/// The gateway a device test's peers are minted with: it runs the platform half of each verb and
/// nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Devices and requests act through <see cref="INearbyDeviceGateway"/>, so a request read straight
/// off the platform's channel needs one before it can be answered at all. These tests cover the
/// platform layer with no session above it, so the gateway forwards the two request verbs to the
/// request's own <c>AcceptCore</c>/<c>RejectCore</c> — the platform halves a session would run
/// after its bookkeeping — and refuses the session-level members, which no device test means to
/// reach.
/// </para>
/// <para>
/// The session's own bookkeeping is covered by the unit suite, whose fake mints devices the same
/// way against a real session.
/// </para>
/// </remarks>
sealed class PlatformOnlyGateway : INearbyDeviceGateway
{
    public Task<NearbyConnection> AcceptRequestAsync(
        NearbyConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        // MarkAnswered before the platform half, as the session does. It releases the expiry
        // registration, so the offer's own deadline cannot also fire the reject and consume the
        // one-shot invitation handler that the accept path's failure exit needs.
        request.MarkAnswered();

        return request.AcceptCore(cancellationToken);
    }

    public Task DeclineRequestAsync(
        NearbyConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        request.MarkAnswered();

        return request.RejectCore(cancellationToken);
    }

    public Task<NearbyConnection> ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
        => throw NoSession(nameof(ConnectAsync));

    public Task DisconnectAsync(string deviceId)
        => throw NoSession(nameof(DisconnectAsync));

    public bool TryGetConnection(string deviceId, [NotNullWhen(true)] out NearbyConnection? connection)
        => throw NoSession(nameof(TryGetConnection));

    public bool TryGetRequest(string deviceId, [NotNullWhen(true)] out NearbyConnectionRequest? request)
        => throw NoSession(nameof(TryGetRequest));

    public bool TryGetConnectDeadline(string deviceId, [NotNullWhen(true)] out DateTimeOffset? deadline)
        => throw NoSession(nameof(TryGetConnectDeadline));

    static InvalidOperationException NoSession(string member)
        => new($"{member} needs a session. Device tests cover the platform layer; use the unit suite for session bookkeeping.");
}
