namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// A payload that arrived from a nearby device, together with the device that sent it.
/// </summary>
/// <remarks>
/// <para>
/// What <see cref="INearbyConnectivity.PayloadsAsync(CancellationToken)"/> yields. One merged loop
/// serves every peer, so each payload has to say who sent it. Reaching back to a connection to ask
/// is exactly the per-connection bookkeeping that stream exists to remove.
/// </para>
/// <para>
/// <b>Both members are always present.</b> The sender is part of what it means to receive
/// something, so this type exists rather than a nullable property on
/// <see cref="NearbyPayload"/> itself: a payload an application constructed has no sender, and
/// making every read site prove that at the point of use is a cost paid for a case the receive
/// loop never sees.
/// </para>
/// <para>
/// A class rather than a <c>record struct</c>, so there is no <c>default</c> instance carrying a
/// null device and a null payload. The API has no way to produce one, and a caller
/// deconstructing what this stream yields never has to check.
/// </para>
/// </remarks>
/// <param name="RemoteDevice">
/// The device that sent the payload, the same immutable snapshot
/// <see cref="NearbyConnection.RemoteDevice"/> carries. Its data is frozen at the moment the
/// connection opened, while its verbs (<see cref="NearbyDevice.ConnectAsync(CancellationToken)"/>,
/// <see cref="NearbyDevice.Connection"/>) resolve against the session when called.
/// </param>
/// <param name="Payload">
/// What arrived. Pattern-match it: <see cref="NearbyBytesPayload"/>,
/// <see cref="NearbyFilePayload"/>, or <see cref="NearbyStreamPayload"/>, the closed set.
/// </param>
/// <seealso cref="INearbyConnectivity.PayloadsAsync(CancellationToken)"/>
/// <seealso href="https://phunkeler.github.io/Plugin.Maui.NearbyConnectivity/docs/payloads.html">Send and receive payloads</seealso>
public sealed record NearbyInboundPayload(NearbyDevice RemoteDevice, NearbyPayload Payload);
