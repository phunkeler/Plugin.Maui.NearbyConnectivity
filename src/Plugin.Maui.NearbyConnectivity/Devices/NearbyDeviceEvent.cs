namespace Plugin.Maui.NearbyConnectivity;

// A discovery result the session applies to its Devices collection: Found is false for a lost
// device.
sealed record NearbyDeviceEvent(NearbyDevice Device, bool Found);
