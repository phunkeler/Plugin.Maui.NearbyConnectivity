using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.ApplicationModel;

namespace Plugin.Maui.NearbyConnectivity.DeviceTests.Native;

/// <summary>
/// Wiring smoke for the background-suspension observer — the shipped-but-never-hardware-verified
/// path from the iOS backgrounding work. Exercises the real <c>NSNotificationCenter</c>
/// registration and callback marshaling on a live UIKit runtime.
/// </summary>
/// <remarks>
/// The observer's whole job is to raise its callback when iOS posts the backgrounding
/// notification, so the callback is what these tests observe. What they pin is the class of
/// failure a compile check cannot catch — selector and thread marshaling on a real runtime — and
/// that a disposed observer has genuinely unregistered rather than leaving a stale registration
/// behind.
/// </remarks>
public sealed class AppLifecycleObserverTests : DeviceTest
{
    [Fact]
    public async Task BackgroundNotification_RaisesTheSuspendedSignal()
    {
        // Arrange
        var suspended = 0;
        using var observer = new AppLifecycleObserver(() => Interlocked.Increment(ref suspended), NullLogger.Instance);

        // Act
        await PostDidEnterBackgroundAsync();

        // Assert
        // The real notification reached the real registration.
        Assert.Equal(1, Volatile.Read(ref suspended));
    }

    [Fact]
    public async Task Dispose_IsIdempotentAndUnregisters()
    {
        // Arrange
        var suspended = 0;
        var observer = new AppLifecycleObserver(() => Interlocked.Increment(ref suspended), NullLogger.Instance);

        // Act
        // Dispose twice, then post: a stale registration would invoke a disposed observer.
        observer.Dispose();
        observer.Dispose();
        await PostDidEnterBackgroundAsync();

        // Assert
        Assert.Equal(0, Volatile.Read(ref suspended));
    }

    /// <summary>
    /// Posts the notification iOS itself posts on backgrounding, on the main thread.
    /// </summary>
    /// <remarks>
    /// Main thread because that is where iOS posts it: UIKit's own observers (keyboard, view
    /// layout) are subscribed to this notification and assume main-thread delivery, so posting
    /// from the xUnit worker thread makes them log main-thread violations.
    /// </remarks>
    static Task PostDidEnterBackgroundAsync()
        => MainThread.InvokeOnMainThreadAsync(() =>
            NSNotificationCenter.DefaultCenter.PostNotificationName(
                UIApplication.DidEnterBackgroundNotification, null));
}
