# Test code that uses the plugin

Your view models and services depend on [`INearbyConnectivity`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity), so fake that interface and test them
on the desktop. You need no device and no emulator.

Put your view models and services in a `net10.0` class library, and reference that library from
both the app and the tests. A MAUI app targets `net10.0-android` and `net10.0-ios`, which a desktop
test project cannot reference.

## Target `net10.0` from the test project

The package also has a plain `net10.0` target, so a test project can reference it. That target
exists for compilation, not for use at run time. On it, [`CheckAvailabilityAsync`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.CheckAvailabilityAsync*) reports
[`NearbyAvailability.Unsupported`](xref:Plugin.Maui.NearbyConnectivity.NearbyAvailability.Unsupported) and every start method throws [`NearbyException`](xref:Plugin.Maui.NearbyConnectivity.NearbyException).

## Fake the interfaces

`INearbyConnectivity`, [`INearbyAdvertiser`](xref:Plugin.Maui.NearbyConnectivity.INearbyAdvertiser), and [`INearbyDiscoverer`](xref:Plugin.Maui.NearbyConnectivity.INearbyDiscoverer) are public interfaces, so a
mocking library can fake them. The examples use [NSubstitute](https://nsubstitute.github.io/).

```csharp
var nearby = Substitute.For<INearbyConnectivity>();
```

To test a failure path, return the real exception type:

```csharp
nearby.Discoverer.StartAsync(Arg.Any<CancellationToken>())
    .Returns(Task.FromException(new NearbyException("Bluetooth is off.")));
```

The real session raises its events on a thread-pool thread. A fake raises them on the test's
thread. Code that dispatches to the UI thread works with both.

## Build devices for the fake to report

[`NearbyDevice`](xref:Plugin.Maui.NearbyConnectivity.NearbyDevice) has a public constructor for tests. Build a device with the status that your test
needs, and raise [`DeviceChanged`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.DeviceChanged) with it.

```csharp
var ana = new NearbyDevice("a1", new NearbyPresence { DisplayName = "Ana" })
{
    Status = NearbyDeviceStatus.Connected,
};

nearby.Devices.Returns([ana]);
nearby.DeviceChanged += Raise.Event<EventHandler<NearbyDeviceChange>>(
    nearby, new NearbyDeviceChange(NearbyDeviceChangeAction.Added, ana));
```

A device built this way belongs to no session, so [`ConnectAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyDevice.ConnectAsync*) and [`DisconnectAsync`](xref:Plugin.Maui.NearbyConnectivity.NearbyDevice.DisconnectAsync*) throw
[`InvalidOperationException`](xref:System.InvalidOperationException).

[`NearbyConnection`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnection) and [`NearbyConnectionRequest`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectionRequest) are sealed, and their constructors are internal. A
test cannot create them, and a mocking library cannot fake them. To test code that sends or
accepts, put that call behind a method that your view model owns. Then fake that method.

## Register the plugin in a bare host

[`AddNearbyConnectivity`](xref:Plugin.Maui.NearbyConnectivity.NearbyConnectivityServiceCollectionExtensions.AddNearbyConnectivity*) also exists on `IServiceCollection`, for a host without a `MauiAppBuilder`.
It works where nobody called [`AddLogging`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.dependencyinjection.loggingservicecollectionextensions.addlogging). It also
registers a [`TimeProvider`](xref:System.TimeProvider) if the host registered none. It validates the options, so an invalid
[`ServiceId`](xref:Plugin.Maui.NearbyConnectivity.NearbyOptions.ServiceId) throws at the registration call. The first registration wins: a second call registers
nothing, and its options are ignored.

```csharp
var services = new ServiceCollection();
services.AddNearbyConnectivity(o => o.ServiceId = "tests");
```

## Next

- [Samples](index.md#samples) for two apps that you can run.
- [CONTRIBUTING.md](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/blob/main/CONTRIBUTING.md) if you contribute to the plugin itself.
