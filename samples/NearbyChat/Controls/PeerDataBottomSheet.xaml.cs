using NearbyChat.ViewModels;
using Plugin.Maui.BottomSheet;

namespace NearbyChat.Controls;

public partial class PeerDataBottomSheet : BottomSheet
{
    public PeerDataBottomSheet(PeerDataViewModel viewModel)
    {
        BindingContext = viewModel;
        InitializeComponent();

        Opening += (_, _) => SheetScrim.Opening();
    }
}
