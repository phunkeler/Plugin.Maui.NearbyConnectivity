namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// The session half of the runtime presence: two members that relay to the transport, which owns
/// the presence and the idle span that decides when a change takes effect.
/// </summary>
/// <remarks>
/// The transport is where the span is really computed, so <c>NearbyTransportTests.RuntimePresence</c>
/// is what pins that. These cover what a consumer sees through <see cref="INearbyConnectivity"/>:
/// the getter reports the effective presence, never one still waiting.
/// </remarks>
[Trait("Category", "Session")]
public sealed class PresenceTests
{
    [Fact]
    public void SetWhileIdle_CarriesTheInfoToo()
    {
        // Arrange
        var platform = new FakeNearby();
        var session = Create.Session(platform);

        // Act
        session.SetPresence(new() { DisplayName = "Bob", Info = "route:R17"u8.ToArray() });

        // Assert
        Assert.Equal("route:R17"u8.ToArray(), session.Presence.Info.ToArray());
    }

    [Fact]
    public void SetNull_Throws()
    {
        // Arrange
        var session = Create.Session(new FakeNearby());

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => session.SetPresence(null!));
    }

    [Fact]
    public void SetWhileIdle_IsWhatTheGetterReports()
    {
        // Arrange
        var platform = new FakeNearby();
        var session = Create.Session(platform);

        // Act
        session.SetPresence(new() { DisplayName = "Bob" });

        // Assert
        Assert.Equal("Bob", session.Presence.DisplayName);
    }

    [Fact]
    public void SetWhileRunning_TheGetterStillReportsTheEffectiveName()
    {
        // What peers are seeing is the useful value to bind to. Reporting the pending one would
        // tell a view the rename had happened while the old name was still on the air.

        // Arrange
        var platform = new FakeNearby { IsIdle = false };
        var session = Create.Session(platform);
        var before = session.Presence.DisplayName;

        // Act
        session.SetPresence(new() { DisplayName = "Bob" });

        // Assert
        Assert.Equal(before, session.Presence.DisplayName);
        Assert.NotEqual("Bob", session.Presence.DisplayName);

        // And once the span ends, it takes effect.
        platform.GoIdle();
        Assert.Equal("Bob", session.Presence.DisplayName);
    }

    [Fact]
    public void AnInvalidName_Throws()
    {
        // Arrange
        var platform = new FakeNearby();
        var session = Create.Session(platform);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => session.SetPresence(new() { DisplayName = "" }));
    }
}
