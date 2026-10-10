namespace Plugin.Maui.NearbyConnectivity.UiTests.Host;

public sealed class App : Application
{
    readonly AppShell _shell;

    public App(AppShell shell) => _shell = shell;

    protected override Window CreateWindow(IActivationState? activationState) => new(_shell);
}
