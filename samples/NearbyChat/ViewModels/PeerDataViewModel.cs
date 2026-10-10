using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plugin.Maui.BottomSheet.Navigation;

namespace NearbyChat.ViewModels;

/// <summary>
/// The detail sheet behind a connected device's data chip.
/// </summary>
/// <remarks>
/// Binds to the row that opened it rather than copying its numbers, so a transfer that finishes
/// while the sheet is open updates it.
/// </remarks>
public partial class PeerDataViewModel(IBottomSheetNavigationService bottomSheetNavigationService)
    : ObservableObject, INavigationAware
{
    /// <summary>
    /// The connected device row whose traffic the sheet shows.
    /// </summary>
    [ObservableProperty]
    public partial ConnectedDeviceViewModel? Row { get; private set; }

    /// <inheritdoc/>
    public void OnNavigatedFrom(IBottomSheetNavigationParameters parameters)
    {
        Row?.SetDataShown(false);
    }

    /// <inheritdoc/>
    public void OnNavigatedTo(IBottomSheetNavigationParameters parameters)
    {
        if (parameters.TryGetValue(nameof(ConnectedDeviceViewModel), out var row)
            && row is ConnectedDeviceViewModel connected)
        {
            Row = connected;
            connected.SetDataShown(true);
        }
    }

    /// <summary>
    /// Closes the sheet.
    /// </summary>
    [RelayCommand]
    async Task Close() => await bottomSheetNavigationService.GoBackAsync();

    [RelayCommand]
    async Task Verify()
    {
        var row = Row;

        await bottomSheetNavigationService.GoBackAsync();

        if (row is not null)
        {
            await row.ChatCommand.ExecuteAsync(null);
        }
    }
}
