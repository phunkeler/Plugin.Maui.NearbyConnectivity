namespace Plugin.Maui.NearbyConnectivity;

// Every message the plugin logs is declared here, in one partial declaration. SYSLIB1006 (an
// error in .editorconfig) compares ids only within one declaration, so keep messages here rather
// than in platform files. The one exception is 1107 in NearbyLog.ios.cs: it takes MCSessionState,
// and formatting that at the call site would run with logging off. A condition that both
// platforms raise is one method with one id.
//
// Ids are grouped by area in blocks of 100, listed in docs/logging.md. From 1.0.0-preview.1 an id
// names one message for good: never renumber or reuse one, and add a new message at the end of
// its block.
//
// Levels follow the table in docs/logging.md. A failure that is also thrown to a caller logs at
// Debug: the exception is the report, and an app that handles it must not get an Error entry.
static partial class NearbyLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "Start was blocked: {Blockers}.")]
    internal static partial void StartBlocked(ILogger logger, NearbyAvailability blockers);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug, Message = "Advertising failed to start.")]
    internal static partial void AdvertisingStartFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "Discovery failed to start.")]
    internal static partial void DiscoveryStartFailed(ILogger logger, Exception exception);

    // The pumps are awaited by nothing, so this entry is the only report of the failure.
    [LoggerMessage(EventId = 1003, Level = LogLevel.Error, Message = "Advertising stopped unexpectedly and is no longer active.")]
    internal static partial void AdvertisingStopped(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Error, Message = "Discovery stopped unexpectedly and is no longer active.")]
    internal static partial void DiscoveryStopped(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Error, Message = "Advertising failed to start after its stream had completed, so the caller sees a normal end of stream instead of this error.")]
    internal static partial void AdvertisingStartFaultDropped(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Error, Message = "Discovery failed to start after its stream had completed, so the caller sees a normal end of stream instead of this error.")]
    internal static partial void DiscoveryStartFaultDropped(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Warning, Message = "Failed to disconnect device {DeviceId} while stopping the session. Teardown continued.")]
    internal static partial void StopDisconnectFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Warning, Message = "Failed to reject the outstanding request from device {DeviceId} while stopping the session. Teardown continued.")]
    internal static partial void StopRejectFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Error, Message = "Failed to stop the session cleanly during disposal.")]
    internal static partial void DisposeFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Warning, Message = "Session tasks did not finish within {Seconds}s of stopping. A task that is still running may change device state after the stop.")]
    internal static partial void SessionTaskJoinTimedOut(ILogger logger, double seconds);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Information, Message = "The app entered the background. Stopping the session, because MultipeerConnectivity does not survive suspension: connections close and advertising and discovery stop. Start again when the app returns to the foreground.")]
    internal static partial void BackgroundTeardown(ILogger logger);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Error, Message = "Failed to stop the session after the platform suspended the app. Session state may not show that the platform already closed the connections.")]
    internal static partial void SuspendTeardownFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1013, Level = LogLevel.Debug, Message = "Created the local MultipeerConnectivity peer for this session.")]
    internal static partial void LocalPeerCreated(ILogger logger);

    [LoggerMessage(EventId = 1014, Level = LogLevel.Debug, Message = "Released the MultipeerConnectivity session: no advertising, discovery or connection uses it.")]
    internal static partial void PlatformSessionReleased(ILogger logger);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Debug, Message = "Found device {DeviceId}.")]
    internal static partial void DeviceFound(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Debug, Message = "Lost device {DeviceId}.")]
    internal static partial void DeviceLost(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Debug, Message = "Connected device {DeviceId} stopped advertising. The connection stays open.")]
    internal static partial void ConnectedDeviceStoppedAdvertising(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Error, Message = "Discovery refresh failed. Devices that went out of range may stay listed until discovery restarts.")]
    internal static partial void DiscoveryRefreshFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Trace, Message = "Tracking the MultipeerConnectivity peer of device {DeviceId}.")]
    internal static partial void PeerTracked(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 1105, Level = LogLevel.Trace, Message = "Stopped tracking the MultipeerConnectivity peer of device {DeviceId}.")]
    internal static partial void PeerUntracked(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 1106, Level = LogLevel.Debug, Message = "Cannot connect to device {DeviceId}: no MultipeerConnectivity peer is tracked for it.")]
    internal static partial void PeerNotFound(ILogger logger, string deviceId);

    // 1107 PeerStateChanged is in NearbyLog.ios.cs.

    [LoggerMessage(EventId = 1200, Level = LogLevel.Debug, Message = "Received a connection request from device {DeviceId}.")]
    internal static partial void RequestReceived(ILogger logger, string deviceId);

    // Debug, not Warning: a peer on an older plugin version sends no frame on every request, so
    // this is a routine cross-version condition, not a fault.
    [LoggerMessage(EventId = 1201, Level = LogLevel.Debug, Message = "The connection request from device {DeviceId} carried no valid connect-request frame. The default offer window applies.")]
    internal static partial void RequestFrameMissing(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Debug, Message = "Device {DeviceId} declared a {DeclaredSeconds}s offer window. It was clamped to {ClampedSeconds}s.")]
    internal static partial void OfferWindowClamped(ILogger logger, string deviceId, double declaredSeconds, double clampedSeconds);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Debug, Message = "Device {DeviceId} sent a new connection request while its previous one was outstanding. The previous request was withdrawn, and the new one is now the outstanding request.")]
    internal static partial void RequestReplaced(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Debug, Message = "Device {DeviceId} sent a connection request while its connection is open. The request was declined, and the connection stays open.")]
    internal static partial void RequestFromConnectedDevice(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 1205, Level = LogLevel.Debug, Message = "The connection request from device {DeviceId} was not answered before its offer deadline and was rejected.")]
    internal static partial void RequestExpired(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 1206, Level = LogLevel.Warning, Message = "Failed to reject the expired connection request from device {DeviceId}. The device returned to Visible, but the platform may still hold the request open.")]
    internal static partial void RequestExpiryRejectFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(EventId = 1207, Level = LogLevel.Error, Message = "The expiry countdown for the connection request from device {DeviceId} failed. The request may stay outstanding until the session stops.")]
    internal static partial void RequestExpiryFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(EventId = 1208, Level = LogLevel.Error, Message = "The consent predicate threw for device {DeviceId}. The request was held for the application instead.")]
    internal static partial void ConsentPredicateFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(EventId = 1209, Level = LogLevel.Warning, Message = "Failed to reject the request from device {DeviceId} under the consent policy.")]
    internal static partial void ConsentRejectFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(EventId = 1210, Level = LogLevel.Debug, Message = "The consent decision for device {DeviceId} did not arrive before the offer expired or advertising stopped. The request was declined, and a later answer is ignored.")]
    internal static partial void ConsentDecisionAbandoned(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 1211, Level = LogLevel.Error, Message = "Automatically accepting the connection request from device {DeviceId} failed. No application code started this accept, so this entry is the only report of the failure.")]
    internal static partial void AutoAcceptFailed(ILogger logger, string deviceId, Exception exception);

    // Warning, not Error: unlike AutoAcceptFailed there is no fault to carry. The remote peer
    // never completed the handshake it asked for, and the bound this session owns ended it.
    [LoggerMessage(EventId = 1212, Level = LogLevel.Warning, Message = "The automatically accepted connection request from device {DeviceId} did not complete its handshake within {BoundSeconds}s and was abandoned.")]
    internal static partial void AutoAcceptTimedOut(ILogger logger, string deviceId, double boundSeconds);

    // Debug, not Information: a handshake that ends without a connection is an ordinary outcome,
    // and this fires once per attempt. Information is on by default in a consumer's app.
    [LoggerMessage(EventId = 1213, Level = LogLevel.Debug, Message = "The handshake with device {DeviceId} ended before a connection opened: {Reason}.")]
    internal static partial void HandshakeEnded(ILogger logger, string deviceId, NearbyEndReason reason);

    [LoggerMessage(EventId = 1214, Level = LogLevel.Debug, Message = "Sent the connection request to device {DeviceId} over the link that is still open from an earlier connection.")]
    internal static partial void ConnectRequestOverLiveLink(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 1215, Level = LogLevel.Debug, Message = "Connection result for device {DeviceId}: success {IsSuccess}, status {StatusCode} ({StatusMessage}).")]
    internal static partial void ConnectionResult(ILogger logger, string deviceId, int statusCode, string statusMessage, bool isSuccess);

    [LoggerMessage(EventId = 1216, Level = LogLevel.Warning, Message = "Failed to clear platform state for the timed-out connection attempt to device {DeviceId}. A retry may fail until the platform releases the endpoint.")]
    internal static partial void AbandonConnectFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(EventId = 1217, Level = LogLevel.Warning, Message = "Failed to clear platform state after the connection request to device {DeviceId} failed. A retry may fail until the platform releases the endpoint.")]
    internal static partial void StaleConnectionClearFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(EventId = 1218, Level = LogLevel.Debug, Message = "Disconnecting from device {DeviceId}.")]
    internal static partial void Disconnecting(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 1219, Level = LogLevel.Debug, Message = "Device {DeviceId} disconnected.")]
    internal static partial void DeviceDisconnected(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 1220, Level = LogLevel.Debug, Message = "Device {DeviceId} announced that it closed the connection.")]
    internal static partial void PeerAnnouncedDisconnect(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 1221, Level = LogLevel.Error, Message = "Failed to observe the disconnect of device {DeviceId}. It may keep reporting Connected.")]
    internal static partial void DisconnectWatchFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(EventId = 1222, Level = LogLevel.Warning, Message = "Failed to dispose the connection to device {DeviceId}. Teardown continued.")]
    internal static partial void ConnectionDisposeFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(EventId = 1223, Level = LogLevel.Warning, Message = "Closing the connection to device {DeviceId} stopped waiting for its queued work after {TimeoutSeconds}s. Its payload handles were freed, so an inbound copy that is still running may fail.")]
    internal static partial void ConnectionDrainTimedOut(ILogger logger, string deviceId, double timeoutSeconds);

    [LoggerMessage(EventId = 1300, Level = LogLevel.Warning, Message = "Device {DeviceId} connected without an authentication code, so the connection reports an empty code.")]
    internal static partial void AuthenticationCodeMissing(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "The authentication code exchange with device {DeviceId} failed. The connection was refused.")]
    internal static partial void CodeExchangeFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Error, Message = "{Count} authentication code exchanges failed after this device revealed its nonce. Every new connection is refused until advertising and discovery stop and no connection is open. Something nearby may be relaying connections.")]
    internal static partial void CodeExchangesExhausted(ILogger logger, int count);

    [LoggerMessage(EventId = 1303, Level = LogLevel.Warning, Message = "Device {DeviceId} presented a second, different certificate on a live link. Neither is trusted, so connections over this link report an empty code.")]
    internal static partial void CertificateChanged(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 1400, Level = LogLevel.Debug, Message = "Failed to send bytes to device {DeviceId}.")]
    internal static partial void SendBytesFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Debug, Message = "The file transfer to device {DeviceId} stalled: no progress for {TimeoutSeconds}s.")]
    internal static partial void SendFileStalled(ILogger logger, string deviceId, double timeoutSeconds);

    [LoggerMessage(EventId = 1402, Level = LogLevel.Debug, Message = "The file transfer to device {DeviceId} failed.")]
    internal static partial void SendFileFailed(ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(EventId = 1403, Level = LogLevel.Debug, Message = "Cannot send file '{Uri}': it is not a valid file:// or content:// URI.")]
    internal static partial void InvalidFileUri(ILogger logger, string uri);

    [LoggerMessage(EventId = 1404, Level = LogLevel.Debug, Message = "Could not parse '{Uri}' as a file URI.")]
    internal static partial void FileUriParseFailed(ILogger logger, string uri, Exception exception);

    [LoggerMessage(EventId = 1405, Level = LogLevel.Debug, Message = "Failed to open the file to send.")]
    internal static partial void FilePayloadBuildFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1406, Level = LogLevel.Warning, Message = "Could not read the display name of the file to send. A generated name is used.")]
    internal static partial void ContentUriNameUnresolved(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1407, Level = LogLevel.Trace, Message = "Received payload {PayloadId} of type {PayloadType} from device {DeviceId}.")]
    internal static partial void PayloadReceived(ILogger logger, string deviceId, long payloadId, int payloadType);

    [LoggerMessage(EventId = 1408, Level = LogLevel.Trace, Message = "Transfer update for payload {PayloadId} with device {DeviceId}: status {Status}, {BytesTransferred} of {TotalBytes} bytes.")]
    internal static partial void PayloadTransferUpdate(ILogger logger, string deviceId, long payloadId, int status, long totalBytes, long bytesTransferred);

    [LoggerMessage(EventId = 1409, Level = LogLevel.Trace, Message = "Received {Length} bytes from device {DeviceId}.")]
    internal static partial void DataReceived(ILogger logger, string deviceId, long length);

    [LoggerMessage(EventId = 1410, Level = LogLevel.Trace, Message = "{Direction} file {PayloadId} with device {DeviceId}: {BytesTransferred} of {TotalBytes} bytes.")]
    internal static partial void FileTransferProgress(ILogger logger, string deviceId, string direction, long payloadId, long totalBytes, long bytesTransferred);

    [LoggerMessage(EventId = 1411, Level = LogLevel.Debug, Message = "Started receiving file '{ResourceName}' from device {DeviceId}.")]
    internal static partial void FileReceiveStarted(ILogger logger, string deviceId, string resourceName);

    [LoggerMessage(EventId = 1412, Level = LogLevel.Debug, Message = "Finished receiving file '{ResourceName}' from device {DeviceId}: location {Location}, error {Error}.")]
    internal static partial void FileReceiveFinished(ILogger logger, string deviceId, string resourceName, string? location, string? error);

    [LoggerMessage(EventId = 1413, Level = LogLevel.Debug, Message = "Refused a file from device {DeviceId}: it has no open connection. The transfer was canceled.")]
    internal static partial void FileRefused(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 1414, Level = LogLevel.Error, Message = "Payload {PayloadId} from device {DeviceId} was not delivered: {Reason}.")]
    internal static partial void InboundPayloadFailed(ILogger logger, string deviceId, long payloadId, string reason);

    [LoggerMessage(EventId = 1415, Level = LogLevel.Debug, Message = "Payload {PayloadId} from device {DeviceId} was canceled.")]
    internal static partial void InboundPayloadCanceled(ILogger logger, string deviceId, long payloadId);

    [LoggerMessage(EventId = 1416, Level = LogLevel.Error, Message = "File '{ResourceName}' from device {DeviceId} was not delivered: {Reason}.")]
    internal static partial void FileReceiveFailed(ILogger logger, string deviceId, string resourceName, string reason, Exception? exception);

    [LoggerMessage(EventId = 1417, Level = LogLevel.Debug, Message = "Refused half of stream payload {PayloadId} from device {DeviceId}: too many streams from this device wait for their other half.")]
    internal static partial void StreamHalfRefused(ILogger logger, string deviceId, long payloadId);

    [LoggerMessage(EventId = 1418, Level = LogLevel.Error, Message = "Failed to copy received file {Source} to {Destination}.")]
    internal static partial void FileCopyFailed(ILogger logger, string source, string destination, Exception exception);

    [LoggerMessage(EventId = 1419, Level = LogLevel.Debug, Message = "Canceled the copy of received file {Source} to {Destination} during teardown.")]
    internal static partial void FileCopyCanceled(ILogger logger, string source, string destination);

    [LoggerMessage(EventId = 1420, Level = LogLevel.Error, Message = "Failed to delete staged received file {Path}.")]
    internal static partial void StagedFileDeleteFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(EventId = 1421, Level = LogLevel.Error, Message = "Failed to delete the platform's copy of received file {Path}.")]
    internal static partial void PlatformFileDeleteFailed(ILogger logger, string path, Exception exception);

    // Logged once per connection, not once per payload: this fires on a hot path, and a consumer
    // that never reads its payloads would otherwise get one warning for every message received.
    [LoggerMessage(EventId = 1422, Level = LogLevel.Warning, Message = "{Count} payloads from device {DeviceId} are buffered and nothing has read them. Nothing is lost, but memory grows until a consumer reads them with INearbyConnectivity.PayloadsAsync.")]
    internal static partial void PayloadsUnread(ILogger logger, string deviceId, int count);

    [LoggerMessage(EventId = 1423, Level = LogLevel.Warning, Message = "A payload from device {DeviceId} arrived while no connection to it was open, and was dropped.")]
    internal static partial void PayloadWithoutConnection(ILogger logger, string deviceId);

    [LoggerMessage(EventId = 1424, Level = LogLevel.Warning, Message = "Disposal stopped waiting for queued work on {PendingPeerCount} devices after {TimeoutSeconds}s. A staged file that a copy was still writing may be deleted by the sweep that follows.")]
    internal static partial void PayloadDrainTimedOut(ILogger logger, int pendingPeerCount, double timeoutSeconds);

    [LoggerMessage(EventId = 1500, Level = LogLevel.Warning, Message = "Re-reading availability after a platform state change failed. The cached value may be stale until the next check.")]
    internal static partial void AvailabilityRefreshFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1501, Level = LogLevel.Warning, Message = "Could not determine {Condition} while checking availability. It is reported as not blocking a start.")]
    internal static partial void AvailabilityCheckIncomplete(ILogger logger, string condition, Exception exception);

    [LoggerMessage(EventId = 1502, Level = LogLevel.Warning, Message = "Could not watch Bluetooth and Wi-Fi state. Availability updates only when it is checked or a start is attempted.")]
    internal static partial void AvailabilityWatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1503, Level = LogLevel.Debug, Message = "Unregistering the availability watcher failed because the platform had already removed it.")]
    internal static partial void AvailabilityUnwatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1504, Level = LogLevel.Error, Message = "ServiceId '{ServiceId}' is not valid for MultipeerConnectivity and would crash the process if used to start. {Failures}")]
    internal static partial void ServiceIdInvalid(ILogger logger, string serviceId, string failures);

    [LoggerMessage(EventId = 1505, Level = LogLevel.Error, Message = "This app's Info.plist is missing what MultipeerConnectivity needs, so discovery would start and never find a device. {Failures}")]
    internal static partial void InfoPlistIncomplete(ILogger logger, string failures);

    [LoggerMessage(EventId = 1506, Level = LogLevel.Warning, Message = "This app's Info.plist may be incomplete for MultipeerConnectivity. Starting is allowed; if discovery finds no device, check this first. {Warnings}")]
    internal static partial void InfoPlistSuspect(ILogger logger, string warnings);

    [LoggerMessage(EventId = 1600, Level = LogLevel.Error, Message = "An event handler threw. The remaining handlers still ran.")]
    internal static partial void EventHandlerFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1601, Level = LogLevel.Warning, Message = "More than {Count} events are waiting to be raised. A handler is slow or blocked, and every later event waits behind it.")]
    internal static partial void EventBacklog(ILogger logger, int count);

    [LoggerMessage(EventId = 1602, Level = LogLevel.Error, Message = "A NearbyDeviceCollection stopped updating. It keeps the devices it had and no longer reflects the session.")]
    internal static partial void DeviceCollectionStopped(ILogger logger, Exception exception);

    // {Callback} and {Writer} carry the method name, so one id covers every raising method and a
    // structured sink can still group by name. Pass the name with nameof().
    [LoggerMessage(EventId = 1700, Level = LogLevel.Error, Message = "Platform callback {Callback} failed for device {DeviceId}. The event it carried was lost.")]
    internal static partial void PlatformCallbackFailed(ILogger logger, string callback, string deviceId, Exception exception);

    [LoggerMessage(EventId = 1701, Level = LogLevel.Error, Message = "{Writer} failed for device {DeviceId}. The event it carried was lost.")]
    internal static partial void EventWriteFailed(ILogger logger, string writer, string deviceId, Exception exception);

    [LoggerMessage(EventId = 1702, Level = LogLevel.Debug, Message = "{Writer}: the stream had already completed, so the event for device {DeviceId} was dropped.")]
    internal static partial void EventStreamCompleted(ILogger logger, string writer, string deviceId);

    [LoggerMessage(EventId = 1703, Level = LogLevel.Trace, Message = "Received a {Type} control frame from device {DeviceId}.")]
    internal static partial void ControlFrameReceived(ILogger logger, string deviceId, ControlMessageType type);

    // Debug, not Warning: a peer on a newer plugin version can send a frame type this version
    // does not handle, so this is a routine cross-version condition, not a fault.
    [LoggerMessage(EventId = 1704, Level = LogLevel.Debug, Message = "Ignored a {Type} control frame from device {DeviceId}: this version does not handle it.")]
    internal static partial void ControlFrameUnknown(ILogger logger, string deviceId, ControlMessageType type);

    [LoggerMessage(EventId = 1705, Level = LogLevel.Warning, Message = "Dropped an unreadable control frame from device {DeviceId}: {Length} bytes with the plugin's signature.")]
    internal static partial void ControlFrameUnreadable(ILogger logger, string deviceId, int length);
}
