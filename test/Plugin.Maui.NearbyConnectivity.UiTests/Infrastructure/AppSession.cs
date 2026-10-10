using OpenQA.Selenium.Support.UI;

namespace Plugin.Maui.NearbyConnectivity.UiTests.Infrastructure;

/// <summary>
/// Driver mechanics, and nothing page-shaped. Page objects call these; tests never do.
/// </summary>
sealed class AppSession : IDisposable
{
    // Android's own label for the bottom-bar overflow item. Not one of our tab titles.
    const string MoreTab = "More";

    readonly AppiumDriver _driver;
    readonly bool _isAndroid;
    bool _disposed;

    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromSeconds(15);

    public AppSession()
    {
        _driver = AppiumDriverFactory.Create();
        _isAndroid = _driver is AndroidDriver;
    }

    // MAUI (as of Controls 10.0.41) does not map AutomationId to Android's ContentDescription when a
    // non-empty AutomationId is set — it sets AccessibilityNodeInfo.ViewIdResourceName
    // ("<package>:id/<id>") and clears ContentDescription if it equalled AutomationId (see
    // dotnet/maui's SemanticExtensions.cs). UIAutomator2's "accessibility id" strategy searches
    // content-desc only, so it can never find these elements; MobileBy.Id targets
    // ViewIdResourceName, which is what MAUI actually populates. On iOS the AutomationId does
    // become the accessibility identifier, so XCUITest's accessibility id strategy is correct there.
    By Locator(string automationId)
        => _isAndroid
            ? MobileBy.Id($"{AppiumDriverFactory.AppId}:id/{automationId}")
            : MobileBy.AccessibilityId(automationId);

    public bool Exists(string automationId)
    {
        try
        {
            _ = _driver.FindElement(Locator(automationId));
            return true;
        }
        catch (NoSuchElementException)
        {
            return false;
        }
    }

    /// <summary>
    /// Taps an element, retrying briefly. A caller often just learned the element exists, but a
    /// MAUI page transition can still replace it in the gap before the click lands.
    /// </summary>
    public void Tap(string automationId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (true)
        {
            try
            {
                _driver.FindElement(Locator(automationId)).Click();
                return;
            }
            catch (Exception ex) when (ex is NoSuchElementException or StaleElementReferenceException
                && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(300));
            }
        }
    }

    /// <summary>
    /// Taps a Shell tab by its title, opening the overflow menu first when the tab lives there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both platforms cap the tab bar at five items. With six tabs, slot five becomes "More" and
    /// the rest move behind it, so the last tabs have no node in the bar at all — the cap is the
    /// same, the overflow UI is not. Android opens a bottom sheet whose rows carry the title as
    /// ContentDescription on the clickable node, with the visible label a non-clickable child
    /// TextView: selecting by text finds that child and clicks something that ignores the tap.
    /// iOS pushes a table whose cells carry the title on an accessible StaticText, which the same
    /// accessibility-id lookup finds directly.
    /// </para>
    /// <para>
    /// So the shape is one try-the-bar-then-the-overflow on both, and only the locators differ.
    /// The bar lookup must be scoped to the bar on iOS: a page reached through the overflow shows
    /// its own title in the navigation bar under the same accessibility name, and an unscoped
    /// lookup finds that StaticText, clicks something inert, and reports success — leaving the app
    /// where it was while the caller waits for a page that never loads.
    /// </para>
    /// <para>
    /// Tapping "More" does not always show the list. iOS remembers which overflow page was open
    /// last and returns straight to it, so when that is the page being asked for there is no row
    /// to tap and none ever appears — waiting for one would time out on a navigation that already
    /// succeeded. The caller's own wait for the page is what confirms the result either way.
    /// </para>
    /// </remarks>
    public void TapTab(string title)
    {
        if (TryTap(BarTab(title)))
        {
            return;
        }

        _driver.FindElement(MobileBy.AccessibilityId(MoreTab)).Click();

        // The overflow animates in, and its rows only accept taps once it has settled — but stop
        // as soon as the row is gone from the screen, which is how "already on that page" looks.
        Wait(timeout: null).Until(_ => TryTap(OverflowRow(title)) || !Showing(OverflowRow(title)));
    }

    /// <summary>Whether <paramref name="locator"/> matches anything on screen right now.</summary>
    bool Showing(By locator)
    {
        try
        {
            return _driver.FindElements(locator).Count > 0;
        }
        catch (WebDriverException)
        {
            return false;
        }
    }

    /// <summary>The tab bar's own button for <paramref name="title"/>, and nothing elsewhere on the page.</summary>
    By BarTab(string title)
        => _isAndroid
            ? MobileBy.AccessibilityId(title)
            : MobileBy.IosNSPredicate(
                $"type == 'XCUIElementTypeButton' AND name == '{title}'");

    /// <summary>The overflow row for <paramref name="title"/>, as that platform exposes it.</summary>
    By OverflowRow(string title)
        => _isAndroid
            ? MobileBy.AndroidUIAutomator(
                $"new UiSelector().clickable(true).childSelector(new UiSelector().text(\"{title}\"))")
            : MobileBy.IosNSPredicate(
                $"type == 'XCUIElementTypeStaticText' AND name == '{title}'");

    bool TryTap(By locator)
    {
        try
        {
            _driver.FindElement(locator).Click();
            return true;
        }
        catch (Exception ex) when (ex is NoSuchElementException or StaleElementReferenceException
            or ElementNotInteractableException or InvalidElementStateException)
        {
            return false;
        }
    }

    /// <summary>
    /// Puts <paramref name="value"/> into a text field and dismisses the keyboard it raises.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The trailing newline is the part that matters. On iOS the on-screen keyboard covers the
    /// bottom of the screen, the tab bar included, so a field left focused makes every later
    /// <see cref="TapTab"/> land on the keyboard instead: navigation silently does nothing and the
    /// failure surfaces as a page that never loads, in whatever test runs next rather than in this
    /// one. Submitting resigns first responder and puts the keyboard away.
    /// </para>
    /// <para>
    /// <c>HideKeyboard</c> is not the tool here — this driver answers it with a 400.
    /// </para>
    /// </remarks>
    public void Fill(string automationId, string value)
    {
        var element = _driver.FindElement(Locator(automationId));

        // Clear first: the driver appends to a field that already has a value.
        element.Clear();
        element.SendKeys(value);
        element.SendKeys("\n");
    }

    public string Text(string automationId) => _driver.FindElement(Locator(automationId)).Text;

    public void WaitForElement(string automationId, TimeSpan? timeout = null)
        => Wait(timeout).Until(_ => Exists(automationId));

    public void WaitForText(string automationId, string expected, TimeSpan? timeout = null)
        => Wait(timeout).Until(_ =>
        {
            try
            {
                return string.Equals(Text(automationId), expected, StringComparison.Ordinal);
            }
            catch (Exception ex) when (ex is NoSuchElementException or StaleElementReferenceException)
            {
                return false;
            }
        });

    /// <summary>Waits until an element's text satisfies <paramref name="predicate"/>.</summary>
    public string WaitForText(string automationId, Func<string, bool> predicate, TimeSpan? timeout = null)
    {
        string? last = null;

        Wait(timeout).Until(_ =>
        {
            try
            {
                last = Text(automationId);
                return predicate(last);
            }
            catch (Exception ex) when (ex is NoSuchElementException or StaleElementReferenceException)
            {
                return false;
            }
        });

        return last ?? throw new InvalidOperationException($"'{automationId}' never produced text.");
    }

    /// <summary>Saves a screenshot, used on failure so a red CI run carries evidence.</summary>
    public void SaveScreenshot(string path)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        _driver.GetScreenshot().SaveAsFile(path);
    }

    WebDriverWait Wait(TimeSpan? timeout) => new(_driver, timeout ?? DefaultTimeout)
    {
        PollingInterval = TimeSpan.FromMilliseconds(250),
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _driver.Quit();
        _driver.Dispose();
    }
}
