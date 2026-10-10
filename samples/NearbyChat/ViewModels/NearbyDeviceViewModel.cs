using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.ViewModels;

public abstract partial class NearbyDeviceViewModel : ObservableObject
{
    NearbyDevice _device;
    readonly DateTimeOffset _receivedAt;

    protected NearbyDeviceViewModel(NearbyDevice device, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _device = device;
        TimeProvider = timeProvider;
        _receivedAt = timeProvider.GetUtcNow();
    }

    /// <summary>
    /// The clock the library stamps <see cref="Deadline"/> with. Reading the system clock here
    /// instead would drift from the library's whenever the host registers its own.
    /// </summary>
    protected TimeProvider TimeProvider { get; }

    protected NearbyDevice Device
    {
        get => _device;
        private set
        {
            ArgumentNullException.ThrowIfNull(value);

            _device = value;
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Presence));
            OnPropertyChanged(nameof(IsConnecting));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(State));
            OnDeviceChanged();

            if (IsConnected && !IsDisconnecting)
            {
                ClearExpired();
                BeginLeaving();
            }
        }
    }

    public string Id => Device.Id;
    public string DisplayName => Device.Presence.DisplayName;
    public NearbyPresence Presence => Device.Presence;

    public string ReceivedAgo => (TimeProvider.GetUtcNow() - _receivedAt) switch
    {
        { TotalMinutes: < 1 } => "Just now",
        { TotalHours: < 1 } elapsed => $"{(int)elapsed.TotalMinutes} min ago",
        { TotalDays: < 1 } elapsed => $"{(int)elapsed.TotalHours}h ago",
        _ => string.Empty,
    };

    protected virtual DateTimeOffset? Deadline => null;
    protected virtual TimeSpan Window => TimeSpan.Zero;
    public bool HasWindow => Deadline is not null && !IsExpired;

    public double WindowProgress
    {
        get
        {
            if (Deadline is not { } deadline || Window <= TimeSpan.Zero)
            {
                return 0d;
            }

            return Math.Clamp((deadline - TimeProvider.GetUtcNow()) / Window, 0d, 1d);
        }
    }

    public TimeSpan WindowRemaining
        => Deadline is { } deadline
            ? deadline - TimeProvider.GetUtcNow() is { Ticks: > 0 } left ? left : TimeSpan.Zero
            : TimeSpan.Zero;

    public bool IsExpired
    {
        get;
        private set => SetFlag(ref field, value);
    }

    public bool IsAnnouncingExpiry
    {
        get;
        private set => SetFlag(ref field, value);
    }

    public string Outcome
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Expired";

    public void Update(NearbyDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (string.Equals(device.Id, Device.Id, StringComparison.Ordinal))
        {
            Device = device;
        }
    }

    protected void SettleExpiry()
    {
        if (!IsAnnouncingExpiry)
        {
            return;
        }

        IsAnnouncingExpiry = false;
        RaiseWindowChanged();
    }

    public void MarkExpired()
        => SetExpired(true, "Expired");

    public void MarkFailed()
        => SetExpired(true, "Failed");

    public void ClearExpired()
        => SetExpired(false, "Expired");

    void SetExpired(bool expired, string outcome)
    {
        if (IsExpired == expired)
        {
            return;
        }

        // Outcome first: it is the caption the expiry announcement reads, so it has to be in
        // place before anything reacts to the flags below.
        Outcome = outcome;
        IsExpired = expired;
        IsAnnouncingExpiry = expired;

        OnExpiredChanged();
        RaiseWindowChanged();
    }

    protected virtual void OnExpiredChanged()
    {
    }

    public bool IsDisconnecting
    {
        get;
        private set => SetFlag(ref field, value);
    }

    public bool IsDisconnected
    {
        get;
        private set => SetFlag(ref field, value);
    }

    public void MarkDisconnecting()
    {
        if (IsDisconnecting)
        {
            return;
        }

        IsDisconnecting = true;
        OnPropertyChanged(nameof(CanDisconnect));
    }

    public void MarkDisconnected()
    {
        MarkDisconnecting();

        if (IsDisconnected)
        {
            return;
        }

        IsDisconnected = true;

        BeginLeaving();
    }

    public bool CanDisconnect => !IsDisconnecting;
    public bool IsConnected => Device.Status is NearbyDeviceStatus.Connected;
    public bool IsConnecting => Device.Status is NearbyDeviceStatus.Connecting;

    public DeviceRowState State
        => IsDisconnected ? DeviceRowState.Disconnected
        : IsDisconnecting ? DeviceRowState.Disconnecting
        : IsConnected ? DeviceRowState.Connected
        : IsAnnouncingExpiry ? DeviceRowState.Expired
        : IsConnecting ? DeviceRowState.Connecting
        : HasWindow ? DeviceRowState.Offering
        : DeviceRowState.Idle;

    public static readonly TimeSpan LeaveHold = TimeSpan.FromSeconds(2.5);
    public event EventHandler? Leaving;
    public event EventHandler? Left;

    public bool IsLeaving
    {
        get;
        private set => SetFlag(ref field, value);
    }

    bool _holding;

    protected void BeginLeaving()
    {
        if (_holding || IsLeaving)
        {
            return;
        }

        _holding = true;
        _ = HoldAsync();

        async Task HoldAsync()
        {
            await Task.Delay(LeaveHold, TimeProvider);

            _holding = false;

            if (!ShouldStillLeave)
            {
                return;
            }

            IsLeaving = true;
            Leaving?.Invoke(this, EventArgs.Empty);
        }
    }

    protected virtual bool ShouldStillLeave => true;

    public void CompleteExit()
    {
        if (IsLeaving)
        {
            Left?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? WindowChanged;

    protected void RaiseWindowChanged()
    {
        OnPropertyChanged(nameof(HasWindow));
        OnPropertyChanged(nameof(WindowProgress));
        OnPropertyChanged(nameof(State));
        WindowChanged?.Invoke(this, EventArgs.Empty);
    }

    public virtual void RefreshRelativeTime() => OnPropertyChanged(nameof(ReceivedAgo));

    protected virtual void OnDeviceChanged()
    {
    }

    bool SetFlag(
        ref bool field,
        bool value,
        [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(State));

        return true;
    }
}
