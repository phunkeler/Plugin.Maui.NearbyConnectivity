using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.ViewModels;

/// <summary>
/// The rows a device page shows: nearby devices projected onto row view models, in a collection
/// that can keep a row the session has already dropped for as long as it is still saying why.
/// </summary>
/// <typeparam name="TRow">The row type this page projects.</typeparam>
/// <remarks>
/// <para>
/// This is the row-projection recipe in full, and it lives in the sample rather than the library
/// on purpose. <see cref="NearbyDeviceCollection"/> binds devices directly, which is all most
/// applications need. A page whose rows carry their own state — a command, a countdown, an exit
/// animation — projects that collection onto rows it owns, because the state that has to survive
/// a device's status transitions is the page's, not the library's.
/// </para>
/// <para>
/// Every device page here needs the same thing: a projection, a bound collection, and the
/// add/remove bookkeeping between them. The pages differed in one
/// predicate — whether a dropped row has an outcome of its own left to state — so that is the only
/// thing <see cref="DeviceRows{TRow}"/> asks for.
/// </para>
/// <para>
/// Rows outlive the session's interest in them. A device that connects belongs to the connections
/// list, so the session stops reporting it here — but the card should say the connection succeeded
/// before it goes.
/// </para>
/// </remarks>
public sealed class DeviceRows<TRow>
    where TRow : NearbyDeviceViewModel
{
    /// <summary>
    /// What the session shows post-filter, keyed by device id. Distinct from <see cref="Rows"/>:
    /// a row can be listed here and not shown (it already played its exit), or shown and no longer
    /// listed (it is still playing one).
    /// </summary>
    readonly Dictionary<string, TRow> _listed = new(StringComparer.Ordinal);

    readonly INearbyConnectivity _nearby;
    readonly IDispatcher _dispatcher;
    readonly Func<NearbyDevice, TRow> _project;
    readonly Func<NearbyDevice, bool> _filter;
    readonly Func<TRow, bool> _lingers;
    readonly Func<TRow, bool> _leavesOnItsOwnTerms;
    readonly Action<TRow>? _onLeft;

    /// <summary>
    /// Projects <paramref name="nearby"/>'s device changes onto rows, keeping dropped rows that
    /// <paramref name="lingers"/> accepts until they play their exit. Nothing is watched until
    /// <see cref="Start(CancellationToken)"/>.
    /// </summary>
    /// <param name="nearby">The session to watch.</param>
    /// <param name="dispatcher">The user interface thread's dispatcher. Changes are applied on it.</param>
    /// <param name="project">Builds the row for a device that has just entered the shown set.</param>
    /// <param name="filter">Which devices this page shows. Re-evaluated on every change.</param>
    /// <param name="lingers">
    /// Whether a row the session has dropped still has an outcome to state, and so stays on screen
    /// until its exit completes. Everything else goes at once — a peer that vanished on its own has
    /// no outcome of ours to report.
    /// </param>
    /// <param name="leavesOnItsOwnTerms">
    /// Whether a row that has finished leaving goes even though the session still lists it. This is
    /// narrower than <paramref name="lingers"/> on the advertising page, where a lapsed request
    /// lingers to say so but only a connection releases the row against the session's own view.
    /// Defaults to <paramref name="lingers"/>, which is right wherever the two coincide.
    /// </param>
    /// <param name="onLeft">Invoked once a row has finished leaving, if the page needs to know.</param>
    public DeviceRows(
        INearbyConnectivity nearby,
        IDispatcher dispatcher,
        Func<NearbyDevice, TRow> project,
        Func<NearbyDevice, bool> filter,
        Func<TRow, bool> lingers,
        Func<TRow, bool>? leavesOnItsOwnTerms = null,
        Action<TRow>? onLeft = null)
    {
        ArgumentNullException.ThrowIfNull(nearby);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(lingers);

        _nearby = nearby;
        _dispatcher = dispatcher;
        _project = project;
        _filter = filter;
        _lingers = lingers;
        _leavesOnItsOwnTerms = leavesOnItsOwnTerms ?? lingers;
        _onLeft = onLeft;
    }

    /// <summary>
    /// The rows to bind. Stable for the life of this instance.
    /// </summary>
    public ObservableCollection<TRow> Rows { get; } = [];

    /// <summary>
    /// Follows the session's devices until <paramref name="appearance"/> is cancelled. Call on the
    /// dispatcher's thread, as the page appears.
    /// </summary>
    /// <param name="appearance">Cancelled when the page disappears.</param>
    /// <remarks>
    /// Rows still shown from an earlier appearance are kept and brought up to date rather than
    /// rebuilt, so a page that reappears keeps its cards, and its bound collection, as they were.
    /// When the page disappears the rows stay as they are, so a page animating out does not empty
    /// while it is still on screen. <see cref="NearbyDeviceCollection"/> seeds before its
    /// constructor returns, so the page's first frame shows every known device.
    /// </remarks>
    public void Start(CancellationToken appearance)
    {
        var devices = new NearbyDeviceCollection(_nearby, _dispatcher, _filter);
        void onChanged(object? _, NotifyCollectionChangedEventArgs e) => Apply(e);

        devices.CollectionChanged += onChanged;
        Sync(devices);

        appearance.Register(() =>
        {
            devices.CollectionChanged -= onChanged;
            devices.Dispose();
        });
    }

    /// <summary>
    /// Brings the listed rows in line with <paramref name="devices"/>: existing rows take the newer
    /// snapshot, new devices are projected, and devices that went while the page was hidden drop.
    /// </summary>
    void Sync(NearbyDeviceCollection devices)
    {
        var shown = new HashSet<string>(StringComparer.Ordinal);

        foreach (var device in devices)
        {
            shown.Add(device.Id);

            if (_listed.TryGetValue(device.Id, out var existing))
            {
                existing.Update(device);
            }
            else
            {
                _listed[device.Id] = _project(device);
            }
        }

        foreach (var id in _listed.Keys.Where(id => !shown.Contains(id)).ToList())
        {
            _listed.Remove(id);
        }

        Reconcile();
    }

    /// <summary>
    /// Applies one change to the filtered devices. Runs on the dispatcher's thread.
    /// </summary>
    void Apply(NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                foreach (NearbyDevice device in e.NewItems ?? Array.Empty<NearbyDevice>())
                {
                    _listed[device.Id] = _project(device);
                }

                Reconcile();
                break;

            case NotifyCollectionChangedAction.Replace:
                // The row is reused, so it keeps its commands, its countdown, and any exit in
                // progress across the device's status transitions.
                foreach (NearbyDevice device in e.NewItems ?? Array.Empty<NearbyDevice>())
                {
                    if (_listed.TryGetValue(device.Id, out var row))
                    {
                        row.Update(device);
                    }
                }

                break;

            case NotifyCollectionChangedAction.Remove:
                foreach (NearbyDevice device in e.OldItems ?? Array.Empty<NearbyDevice>())
                {
                    _listed.Remove(device.Id);
                }

                Reconcile();
                break;
        }
    }

    /// <summary>
    /// Brings <see cref="Rows"/> back in line with what the session lists, honouring the two
    /// predicates that let a row arrive late or leave late.
    /// </summary>
    void Reconcile()
    {
        foreach (var row in _listed.Values)
        {
            // A row that already played its exit stays gone, even though the session still lists
            // the device.
            if (!Rows.Contains(row) && !row.IsLeaving)
            {
                row.Left += OnRowLeft;
                Rows.Add(row);
            }
        }

        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            var row = Rows[i];

            if (!IsListed(row) && !_lingers(row))
            {
                row.Left -= OnRowLeft;
                Rows.RemoveAt(i);
            }
        }
    }

    void OnRowLeft(object? sender, EventArgs e)
    {
        if (sender is not TRow row)
        {
            return;
        }

        // Only drop a row the session itself has let go — unless it left on its own terms, in
        // which case this page is done with it whatever the session still thinks.
        if (!_leavesOnItsOwnTerms(row) && IsListed(row))
        {
            return;
        }

        row.Left -= OnRowLeft;
        Rows.Remove(row);

        _onLeft?.Invoke(row);
    }

    bool IsListed(TRow row) => _listed.TryGetValue(row.Id, out var listed) && ReferenceEquals(listed, row);
}
