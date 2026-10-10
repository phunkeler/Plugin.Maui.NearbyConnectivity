# Bind to a MAUI page

Two types replace the event handlers and the dispatch code that you would write by hand. Both raise
[`PropertyChanged`](xref:System.ComponentModel.INotifyPropertyChanged.PropertyChanged) on the UI thread, and the owner of each one disposes it. Each constructor takes
the [`IDispatcher`](xref:Microsoft.Maui.Dispatching.IDispatcher) of the UI thread. You can omit the dispatcher when you construct the type on the
UI thread.

## Bind a device list

[`NearbyDeviceCollection`](xref:Plugin.Maui.NearbyConnectivity.NearbyDeviceCollection) handles [`DeviceChanged`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.DeviceChanged), applies every change on the dispatcher that you
give it, and raises [`INotifyCollectionChanged`](xref:System.Collections.Specialized.INotifyCollectionChanged). Expose one from your view model:

```csharp
public sealed class DevicesViewModel : IDisposable
{
    public NearbyDeviceCollection Devices { get; }

    public DevicesViewModel(INearbyConnectivity nearby, IDispatcher dispatcher)
        => Devices = new NearbyDeviceCollection(nearby, dispatcher);

    public void Dispose() => Devices.Dispose();
}
```

Then bind a [`CollectionView`](https://learn.microsoft.com/dotnet/api/microsoft.maui.controls.collectionview) directly to the collection. [`NearbyDevice`](xref:Plugin.Maui.NearbyConnectivity.NearbyDevice) exposes
[`Presence.DisplayName`](xref:Plugin.Maui.NearbyConnectivity.NearbyPresence.DisplayName) and [`Status`](xref:Plugin.Maui.NearbyConnectivity.NearbyDevice.Status), so a row needs no converter to show each device:

```xaml
<CollectionView ItemsSource="{Binding Devices}"
                AutomationId="DeviceList">
    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="nearby:NearbyDevice">
            <Grid Padding="12" ColumnDefinitions="*,Auto">
                <Label Text="{Binding Presence.DisplayName}" />
                <Label Grid.Column="1" Text="{Binding Status}" />
            </Grid>
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

`x:DataType` needs the namespace on the page's root element:

```xaml
xmlns:nearby="clr-namespace:Plugin.Maui.NearbyConnectivity;assembly=Plugin.Maui.NearbyConnectivity"
```

[`Count`](xref:Plugin.Maui.NearbyConnectivity.NearbyDeviceCollection.Count) raises `PropertyChanged`, so `{Binding Devices.Count}` stays current.

Read `Count` and the indexer only on the dispatcher's thread.

If your `filter` throws, the collection keeps the devices that it has and stops updating.
[`IsFaulted`](xref:Plugin.Maui.NearbyConnectivity.NearbyDeviceCollection.IsFaulted) becomes `true`, and [`Exception`](xref:Plugin.Maui.NearbyConnectivity.NearbyDeviceCollection.Exception) holds the exception. The collection also logs the
exception through the logger of the plugin's session. If you pass your own [`INearbyConnectivity`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity),
such as a decorator, the collection does not log the exception. The collection throws nothing on
the UI thread.

To show only some devices, pass a `filter`. NearbyChat registers one collection of connected
devices as a singleton in
[`MauiProgram.cs`](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/blob/main/samples/NearbyChat/MauiProgram.cs):

```csharp
builder.Services.AddSingleton(services => new NearbyDeviceCollection(
    services.GetRequiredService<INearbyConnectivity>(),
    services.GetRequiredService<IDispatcher>(),
    static device => device.Status is NearbyDeviceStatus.Connected));
```

Do not bind [`nearby.Devices`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.Devices) directly. It is a read-only snapshot list that raises no change
notification, so the view renders one time and never updates.

## Bind a status line

[`NearbySessionState`](xref:Plugin.Maui.NearbyConnectivity.NearbySessionState) is a bindable view of the session's state. Construct it with the same
dispatcher as the device collection. Its owner disposes it.

```csharp
// Bindings: Status.IsAdvertising, Status.IsDiscovering, Status.IsReady,
// Status.Availability, Status.DiscoveryStop.Reason, Status.DiscoveryStop.Error, and the
// same pair on Status.AdvertisingStop
public NearbySessionState Status { get; }

public StatusViewModel(INearbyConnectivity nearby, IDispatcher dispatcher)
    => Status = new NearbySessionState(nearby, dispatcher);

public void Dispose() => Status.Dispose();
```

`NearbySessionState` merges two sources. [`AvailabilityChanged`](xref:Plugin.Maui.NearbyConnectivity.INearbyConnectivity.AvailabilityChanged) reports radios and permissions.
[`Advertiser.Changed`](xref:Plugin.Maui.NearbyConnectivity.INearbyActivity.Changed) and `Discoverer.Changed` report a fault that stopped advertising or discovery.
Availability alone misses one case. A platform fault reaches only the activity's [`LastStop`](xref:Plugin.Maui.NearbyConnectivity.INearbyActivity.LastStop), so a
label bound to availability shows "Ready" while discovery is stopped.

## Project your own rows

If a row has its own commands or state, project the devices onto your own row type. That state
belongs to the page, not to the library. A row must stay the same object while its device changes
status. A `NearbyDevice` cannot be that object, because each change replaces the immutable snapshot
with a new one.

1. Subscribe to `DeviceChanged`, and dispatch each change to the UI thread.
2. Then read `nearby.Devices`, and apply each device as [`Added`](xref:Plugin.Maui.NearbyConnectivity.NearbyDeviceChangeAction.Added). Because you subscribed first, no
   change is lost between the two steps. Each change has the device's [`Id`](xref:Plugin.Maui.NearbyConnectivity.NearbyDevice.Id) as its key, so a change
   that you apply twice does no harm.
3. Keep an [`ObservableCollection<TRow>`](xref:System.Collections.ObjectModel.ObservableCollection%601) and a dictionary keyed by `device.Id`.
4. For a device that you have not seen, project a row and add it.
5. For a device that you already have, give the new snapshot to the existing row. Do not create
   the row again.
6. On [`Removed`](xref:Plugin.Maui.NearbyConnectivity.NearbyDeviceChangeAction.Removed), or when a device no longer passes your filter, remove the row.
7. Unsubscribe when the page closes.

As an alternative, start from a `NearbyDeviceCollection` and project its [`CollectionChanged`](xref:Plugin.Maui.NearbyConnectivity.NearbyDeviceCollection.CollectionChanged)
events. The collection does steps 1 and 2 for you.
[`samples/NearbyChat/ViewModels/DeviceRows.cs`](https://github.com/phunkeler/Plugin.Maui.NearbyConnectivity/blob/main/samples/NearbyChat/ViewModels/DeviceRows.cs)
is a full example. It includes rows that stay on screen to play an exit after the session removes
them.

## Next

- [Accept and make connections](connect.md) for the methods that a row's commands call.
- [Samples](index.md#samples) for both sample apps.
