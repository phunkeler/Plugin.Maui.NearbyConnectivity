using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using NearbyChat.Services;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.ViewModels;

/// <summary>
/// The base for a page view model, scoping every resource it owns to the window between
/// <see cref="OnAppearing"/> and <see cref="OnDisappearing"/>.
/// </summary>
[SuppressMessage(
    "Microsoft.Design",
    "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable",
    Justification = "The analyzer's fix — implementing IDisposable — is the defect here, not the cure: "
        + "the container roots every disposable transient it creates, and .NET MAUI never calls Dispose "
        + "on a binding context anyway. _appearanceCts is disposed on every OnDisappearing instead, so it "
        + "never outlives the appearance that created it. See the class remarks.")]
public abstract partial class BasePageViewModel(
    IDispatcher dispatcher)
    : ObservableObject
{
    CancellationTokenSource? _appearanceCts;
    IDispatcherTimer? _relativeTimeTimer;
    EventHandler? _relativeTimeTick;

    protected IDispatcher Dispatcher { get; } = dispatcher;

    protected CancellationToken AppearanceToken => _appearanceCts?.Token ?? CancellationToken.None;

    /// <summary>
    /// Why the last start attempt failed, or <see langword="null"/> if none has.
    /// </summary>
    /// <remarks>
    /// Shown on the page rather than as an alert, for the same reason a failed device row keeps its
    /// reason inline: a modal steals the screen from the toggle the user is about to tap again.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailed))]
    public partial string? FailureReason { get; set; }

    /// <summary>
    /// What the user can do about <see cref="FailureReason"/>, or <see langword="null"/> when there
    /// is nothing to suggest beyond trying again.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRemedy))]
    public partial string? FailureRemedy { get; set; }

    /// <summary>
    /// Whether the last start attempt failed and the page is showing its reason.
    /// </summary>
    public bool HasFailed => FailureReason is not null;

    /// <summary>
    /// Raised once a row that connected has been dropped from the page, for the view to draw the
    /// eye to the connections chip it just moved to.
    /// </summary>
    public event EventHandler? ConnectedRowLeft;

    protected void RaiseConnectedRowLeft() => ConnectedRowLeft?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Whether there is a remedy to show alongside <see cref="FailureReason"/>.
    /// </summary>
    public bool HasRemedy => !string.IsNullOrEmpty(FailureRemedy);

    /// <summary>
    /// Records why a start attempt failed, for the page to render.
    /// </summary>
    protected void Fail(string reason, string? remedy = null)
    {
        FailureReason = reason;
        FailureRemedy = remedy;
    }

    /// <summary>
    /// Records something worth telling the user about a start that nonetheless succeeded — Wi-Fi
    /// off, say, which leaves a working but slower connection.
    /// </summary>
    /// <remarks>
    /// Rendered the same way as a failure but reached only when
    /// the start succeeded, so the page never shows a
    /// warning and a failure at once.
    /// </remarks>
    protected void Warn(string reason) => Fail(reason, remedy: null);

    /// <summary>
    /// Clears a stale failure, so a fresh attempt shows what is happening now rather than what
    /// failed last time.
    /// </summary>
    protected void ClearFailure()
    {
        FailureReason = null;
        FailureRemedy = null;
    }

    /// <summary>
    /// Runs one activity toggle — start when <paramref name="isActive"/> is false, stop when it is
    /// true — and reports the outcome the same way on both pages.
    /// </summary>
    /// <param name="noun">
    /// The activity as it appears in a sentence ("Discovery", "Advertising"), used to build the
    /// failure text.
    /// </param>
    /// <param name="isActive">The session's current state for this activity, read fresh.</param>
    /// <param name="startAsync">Starts the activity.</param>
    /// <param name="stopAsync">Stops the activity. Takes no token: teardown is not cancellable.</param>
    /// <param name="availability">The session's availability, read at the moment of failure.</param>
    /// <param name="permissionNeed">
    /// What the permission is for, as it completes "Nearby needs permission to ..." — "find
    /// devices", "advertise".
    /// </param>
    /// <param name="permissionPrompt">
    /// What to tell the user to do when permission has not been granted but the OS will ask again.
    /// </param>
    /// <param name="cancellationToken">Cancelled when the user navigates away mid-toggle.</param>
    /// <remarks>
    /// Only the start path is gated. Stopping needs neither permission nor a working radio, and
    /// refusing to stop because the user revoked something mid-session would strand the toggle on.
    /// </remarks>
    protected async Task ToggleActivityAsync(
        string noun,
        bool isActive,
        Func<CancellationToken, Task> startAsync,
        Func<Task> stopAsync,
        Func<NearbyAvailability> availability,
        string permissionNeed,
        string permissionPrompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startAsync);
        ArgumentNullException.ThrowIfNull(stopAsync);
        ArgumentNullException.ThrowIfNull(availability);

        ClearFailure();

        if (!isActive && !await EnsurePermissionAsync(permissionNeed, permissionPrompt))
        {
            return;
        }

        try
        {
            if (isActive)
            {
                await stopAsync();
            }
            else
            {
                await startAsync(cancellationToken);

                // Wi-Fi off does not block a start — Bluetooth alone still carries a connection,
                // just slowly — so it arrives as a warning after the fact rather than a refusal.
                if (availability() is NearbyAvailability.WifiDisabled)
                {
                    Warn(NearbyAvailabilityText.Describe(availability()));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The user navigated away or cancelled the operation. Nothing to report.
        }
        catch (NearbyException) when (!isActive && availability() is not NearbyAvailability.Available)
        {
            OnToggleUnavailable(availability());

            Fail($"{noun} cannot start yet.", NearbyAvailabilityText.Describe(availability()));
        }
        catch (NearbyException ex)
        {
            OnToggleFailed(isActive, ex);

            Fail(
                isActive
                    ? $"{noun} could not be stopped."
                    : $"{noun} could not be started.",
                "Check your connection settings, then try again.");
        }
    }

    /// <summary>
    /// Logs a start the session refused. Overridden per page so each keeps its own event id.
    /// </summary>
    protected virtual void OnToggleUnavailable(NearbyAvailability availability)
    {
    }

    /// <summary>
    /// Logs a toggle that threw. Overridden per page so each keeps its own event id.
    /// </summary>
    protected virtual void OnToggleFailed(bool wasActive, Exception exception)
    {
    }

    /// <summary>
    /// Whether the platform will let this activity start: the permission prompt, which is a UI
    /// decision. Records the reason and returns <see langword="false"/> when it will not.
    /// </summary>
    /// <remarks>
    /// The start verbs do the availability preflight and the WifiDisabled-is-a-warning policy, so
    /// the permission is all that is left here.
    /// </remarks>
    async Task<bool> EnsurePermissionAsync(string permissionNeed, string permissionPrompt)
    {
        var permission = await Permissions.RequestAsync<NearbyPermission>();

        if (permission is PermissionStatus.Granted)
        {
            return true;
        }

        Fail(
            $"Nearby needs permission to {permissionNeed}.",
            permission is PermissionStatus.Denied
                ? "Permission was denied for good. Enable it in system settings, then try again."
                : permissionPrompt);

        return false;
    }

    protected void TrackRelativeTime(IReadOnlyList<NearbyDeviceViewModel> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        StopRelativeTime();

        _relativeTimeTick = (_, _) =>
        {
            foreach (var row in rows)
            {
                row.RefreshRelativeTime();
            }
        };

        _relativeTimeTimer = Dispatcher.CreateTimer();
        _relativeTimeTimer.Interval = TimeSpan.FromSeconds(30);
        _relativeTimeTimer.Tick += _relativeTimeTick;
        _relativeTimeTimer.Start();
    }

    protected void StopRelativeTime()
    {
        if (_relativeTimeTimer is null)
        {
            return;
        }

        _relativeTimeTimer.Stop();
        _relativeTimeTimer.Tick -= _relativeTimeTick;
        _relativeTimeTimer = null;
        _relativeTimeTick = null;
    }

    protected internal virtual void OnAppearing()
    {
        var old = _appearanceCts;
        _appearanceCts = new CancellationTokenSource();
        old?.Cancel();
        old?.Dispose();
    }

    protected internal virtual void OnDisappearing()
    {
        var old = _appearanceCts;
        _appearanceCts = null;
        old?.Cancel();
        old?.Dispose();

        StopRelativeTime();
        ClearFailure();
    }
}
