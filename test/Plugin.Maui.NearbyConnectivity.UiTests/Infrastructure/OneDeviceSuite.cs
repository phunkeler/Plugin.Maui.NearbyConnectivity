namespace Plugin.Maui.NearbyConnectivity.UiTests.Infrastructure;

/// <summary>
/// Puts every UI test class in one collection sharing one <see cref="UiTestFixture"/>.
/// </summary>
/// <remarks>
/// There is one app on one device, so there can be one driver session. With
/// <c>IClassFixture</c> each test class built its own, and xUnit runs classes in parallel — six
/// sessions raced for the same simulator, each relaunching the app under the others. That showed
/// up as tabs a session could not find and as session creation timing out after 120s, neither of
/// which is what it looks like: the app and the locators were fine.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OneDeviceSuite : ICollectionFixture<UiTestFixture>
{
    /// <summary>The collection name every UI test class carries.</summary>
    public const string Name = "ui";
}
