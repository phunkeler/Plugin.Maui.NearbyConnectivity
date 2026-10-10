namespace Plugin.Maui.NearbyConnectivity;

// Whether an activity runs and how it last stopped, written together and then announced. Announces
// only a real transition, so a repeated start or stop reports nothing.
sealed class ActivityFlag(Action changed)
{
    readonly Lock _gate = new();
    bool _value;
    NearbyActivityStop? _lastStop;

    internal bool Value => Volatile.Read(ref _value);

    internal NearbyActivityStop? LastStop => Volatile.Read(ref _lastStop);

    internal void Set(
        bool value,
        NearbyActivityStopReason reason = NearbyActivityStopReason.Requested,
        Exception? error = null)
    {
        lock (_gate)
        {
            if (_value == value)
            {
                return;
            }

            Volatile.Write(ref _lastStop, value ? null : new NearbyActivityStop(reason, error));
            Volatile.Write(ref _value, value);
            changed();
        }
    }
}
