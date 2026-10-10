using Plugin.Maui.NearbyConnectivity.UiTests.Host.Components;

namespace Plugin.Maui.NearbyConnectivity.UiTests.Host.Pages;

/// <summary>
/// Shared shape for every capability page: a vertical stack, a last-error label, and a token whose
/// lifetime is one appearance of the page, so the streams a page enumerates stop when the user
/// tabs away and start again when they come back.
/// </summary>
public abstract class CapabilityPage : ContentPage, IDisposable
{
    CancellationTokenSource? _cts;

    protected CapabilityPage(string prefix)
    {
        Prefix = prefix;
        Error = new StateBadge($"{prefix}-error") { Text = "none" };
        Stack = new VerticalStackLayout { Padding = 16, Spacing = 8 };
        Content = new ScrollView { Content = Stack };
    }

    protected string Prefix { get; }

    protected VerticalStackLayout Stack { get; }

    /// <summary>
    /// The last failure, surfaced as text. Tests assert that a blocked start reports a reason here
    /// rather than failing silently — the alternative is a dialog, which a test would have to
    /// dismiss before it could read anything else.
    /// </summary>
    protected StateBadge Error { get; }

    /// <summary>Cancelled when the page disappears. Valid only between appearing and disappearing.</summary>
    protected CancellationToken PageToken => _cts?.Token ?? CancellationToken.None;

    /// <summary>Starts the page's live subscriptions. Called on every appearance.</summary>
    protected virtual void OnPageActivated(CancellationToken cancellationToken)
    {
    }

    /// <summary>
    /// Runs a capability call, recording either success or the failure text. Every button on every
    /// page goes through this, so no page needs its own try/catch.
    /// </summary>
    protected async Task GuardAsync(Func<Task> action)
    {
        try
        {
            Error.Set("none");
            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Error.Set($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Pumps an <see cref="IAsyncEnumerable{T}"/> for as long as the page is on screen. A stream
    /// ending by cancellation is the normal exit and is not reported as an error.
    /// </summary>
    protected async void Pump<T>(IAsyncEnumerable<T> source, Action<T> onNext, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(true))
            {
                onNext(item);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Error.Set($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    protected Button AddButton(string id, string text, Func<Task> action)
    {
        var button = new Button { AutomationId = $"{Prefix}-{id}", Text = text };
        button.Clicked += async (_, _) => await GuardAsync(action).ConfigureAwait(true);
        Stack.Add(button);
        return button;
    }

    protected StateBadge AddBadge(string id)
    {
        var badge = new StateBadge($"{Prefix}-{id}");
        Stack.Add(badge);
        return badge;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _cts = new CancellationTokenSource();
        OnPageActivated(_cts.Token);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        GC.SuppressFinalize(this);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
