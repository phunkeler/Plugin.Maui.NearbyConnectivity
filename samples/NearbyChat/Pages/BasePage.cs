using NearbyChat.ViewModels;

namespace NearbyChat.Pages;

public abstract class BasePage<TViewModel> : ContentPage
    where TViewModel : BasePageViewModel
{
    protected BasePage(TViewModel viewModel)
    {
        BindingContext = viewModel;
    }

    protected TViewModel ViewModel => (TViewModel)BindingContext;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ViewModel.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        ViewModel.OnDisappearing();
        base.OnDisappearing();
    }
}
