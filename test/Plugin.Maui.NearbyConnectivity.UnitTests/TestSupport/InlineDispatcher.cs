using Microsoft.Maui.Dispatching;

namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// An <see cref="IDispatcher"/> that runs dispatched work inline on the calling thread, counts it,
/// and keeps whatever it threw.
/// </summary>
/// <remarks>
/// Passed explicitly rather than installed through <c>DispatcherProvider.SetCurrent</c>, which is
/// process-global and would have these tests fighting each other as they run in parallel.
/// <para>
/// <see cref="Fault"/> stands in for the caller's own thread: anything dispatched work throws would
/// crash a real application, so a test asserts it stays <see langword="null"/>.
/// </para>
/// </remarks>
sealed class InlineDispatcher : IDispatcher
{
    /// <summary>How many actions were dispatched.</summary>
    public int Count { get; private set; }

    /// <summary>What dispatched work threw, if any.</summary>
    public Exception? Fault { get; private set; }

    /// <summary>
    /// Always <see langword="true"/>, so every change is handed to <see cref="Dispatch"/> and
    /// counted, even though it then runs inline.
    /// </summary>
    public bool IsDispatchRequired => true;

    public bool Dispatch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Count++;

        try
        {
            action();
        }
        catch (Exception ex)
        {
            Fault = ex;
        }

        return true;
    }

    public bool DispatchDelayed(TimeSpan delay, Action action)
        => throw new NotSupportedException("No test needs a delayed dispatch.");

    public IDispatcherTimer CreateTimer()
        => throw new NotSupportedException("No test needs a dispatcher timer.");
}
