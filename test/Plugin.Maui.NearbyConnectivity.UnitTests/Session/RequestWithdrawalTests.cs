namespace Plugin.Maui.NearbyConnectivity.UnitTests;

/// <summary>
/// Public-surface promises owned by this folder's types.
/// </summary>
[Trait("Category", "Session")]
public sealed class RequestWithdrawalTests
{
    // -------------------------------------------------------------------------
    // stopping advertising withdraws the offers it produced
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StoppingAdvertising_DeclinesOutstandingRequests()
    {
        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        var token = TestContext.Current.CancellationToken;

        await session.Advertiser.StartAsync(token);

        var device = Create.Device("asker");
        var declined = false;

        await platform.EmitRequestAsync(
            device,
            () => Create.Connection(device),
            onReject: () => declined = true);

        var request = await Take.RequestAsync(session, token);

        // Act
        await session.Advertiser.StopAsync();

        // Assert
        Assert.True(declined, "The remote gets a decline rather than waiting out the offer.");
        Assert.False(request.IsPending);
        // Advertising only: the withdrawn asker is dropped, not left as a Visible phantom.
        Assert.Null(session.StatusOf("asker"));
    }

    // -------------------------------------------------------------------------
    // IsPending is false once the request is answered
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AnAnsweredRequest_IsNoLongerPending()
    {
        // IsPending was derived from the expiry task alone, which deliberately never completes for
        // a request answered in time — so a prompt bound to it stayed on screen after the answer.

        // Arrange
        var platform = new FakeNearby();
        await using var session = Create.Session(platform);
        var token = TestContext.Current.CancellationToken;

        await session.Advertiser.StartAsync(token);

        var device = Create.Device("asker");
        await platform.EmitRequestAsync(device, () => Create.Connection(device));

        var request = await Take.RequestAsync(session, token);
        Assert.True(request.IsPending);

        // Act
        await request.AcceptAsync(token);

        // Assert
        // Answered, but not expired — the two outcomes stay distinguishable.
        Assert.False(request.IsPending);
        Assert.False(request.Expired.IsCompleted);
    }
}
