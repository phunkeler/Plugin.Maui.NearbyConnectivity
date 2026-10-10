namespace Plugin.Maui.NearbyConnectivity;

/// <summary>
/// Advertising: makes this device visible to nearby discoverers and answers their connection
/// requests.
/// </summary>
/// <seealso cref="INearbyConnectivity.Advertiser"/>
public interface INearbyAdvertiser : INearbyActivity
{
    /// <summary>
    /// Sets how inbound connection requests are answered, with an optional per-device rule.
    /// </summary>
    /// <param name="policy">What to do when <paramref name="decide"/> makes no decision.</param>
    /// <param name="decide">
    /// Decides per device: <see langword="true"/> to accept, <see langword="false"/> to decline, or
    /// <see langword="null"/> to fall through to <paramref name="policy"/>. When
    /// <see langword="null"/> itself, every request falls through. Its token is canceled when the
    /// request's offer expires or advertising stops.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Replaces both</b> the policy and any per-device rule: calling this without
    /// <paramref name="decide"/> clears a rule installed by an earlier call. It applies to requests
    /// received after it returns; one already outstanding stays answerable.
    /// </para>
    /// <para>
    /// <b><paramref name="decide"/> returning <see langword="null"/> hands the decision back</b>, so
    /// a request a rule cannot settle still reaches a person: the device reports
    /// <see cref="NearbyDeviceStatus.RequestReceived"/> and carries it on
    /// <see cref="NearbyDevice.Request"/>.
    /// </para>
    /// <para>
    /// <b><paramref name="decide"/> may prompt.</b> It runs on its own thread-pool task per request,
    /// so a slow answer holds only that request. The plugin enforces the offer's deadline: when it
    /// passes, or advertising stops, the token is canceled, the request is declined, and an answer
    /// that arrives later is ignored. A <paramref name="decide"/> that throws is logged and the
    /// request is held. Marshal to the UI thread yourself before showing a prompt.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// nearby.Advertiser.SetConsent(
    ///     NearbyConsentPolicy.Hold,
    ///     decide: (device, cancellationToken) =&gt; PromptAsync(device, cancellationToken));
    /// </code>
    /// </example>
    void SetConsent(NearbyConsentPolicy policy, Func<NearbyDevice, CancellationToken, ValueTask<bool?>>? decide = null);
}
