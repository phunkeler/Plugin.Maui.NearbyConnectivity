namespace Plugin.Maui.NearbyConnectivity;

// A named stream arrives as two halves, the name frame and the stream payload, in either order.
// Each half must test for its partner and wait as one atomic step, so one lock guards both maps:
// with a lock for each map, both halves can miss each other and wait, and the stream is never
// delivered.
//
// The peer decides how many halves it sends without a partner. Keep the key scoped to the device
// and keep the limit, or one peer holds memory and open streams that outlive its connection.
sealed class StreamPairing
{
    internal const int MaxWaitingPerDevice = 32;

    readonly Lock _gate = new();
    readonly Dictionary<(string DeviceId, long PayloadId), string> _names = [];
    readonly Dictionary<(string DeviceId, long PayloadId), Stream> _streams = [];

    internal StreamHalf OfferName(string deviceId, long payloadId, string name, out Stream? stream)
    {
        lock (_gate)
        {
            return Offer(
                _streams,
                _names,
                (deviceId, payloadId),
                name,
                out stream);
        }
    }

    internal StreamHalf OfferStream(string deviceId, long payloadId, Stream stream, out string? name)
    {
        lock (_gate)
        {
            return Offer(
                _names,
                _streams,
                (deviceId, payloadId),
                stream,
                out name);
        }
    }

    internal Stream[] Release(string deviceId)
    {
        lock (_gate)
        {
            foreach (var key in _names.Keys.Where(key => key.DeviceId == deviceId).ToArray())
            {
                _names.Remove(key);
            }

            var released = _streams.Where(entry => entry.Key.DeviceId == deviceId).ToArray();

            foreach (var (key, _) in released)
            {
                _streams.Remove(key);
            }

            return [.. released.Select(entry => entry.Value)];
        }
    }

    internal Stream[] Clear()
    {
        lock (_gate)
        {
            Stream[] released = [.. _streams.Values];

            _names.Clear();
            _streams.Clear();

            return released;
        }
    }

    static StreamHalf Offer<TMine, TPartner>(
        Dictionary<(string DeviceId, long PayloadId), TPartner> partners,
        Dictionary<(string DeviceId, long PayloadId), TMine> waiting,
        (string DeviceId, long PayloadId) key,
        TMine mine,
        out TPartner? partner)
    {
        if (partners.Remove(key, out partner))
        {
            return StreamHalf.Paired;
        }

        if (waiting.Keys.Count(held => held.DeviceId == key.DeviceId) >= MaxWaitingPerDevice)
        {
            return StreamHalf.Refused;
        }

        waiting[key] = mine;

        return StreamHalf.Waiting;
    }
}
