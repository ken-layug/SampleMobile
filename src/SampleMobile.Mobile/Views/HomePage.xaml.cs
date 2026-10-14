using SampleMobile.Mobile.Core.ViewModels;

namespace SampleMobile.Mobile.Views;

/// <summary>Status overview of the signed-in session.</summary>
public partial class HomePage : ContentPage {
    private readonly HomeViewModel _viewModel;

    public HomePage(HomeViewModel viewModel) {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing() {
        base.OnAppearing();
        await _viewModel.ActivateAsync();
    }

    protected override void OnDisappearing() {
        _viewModel.Deactivate();
        base.OnDisappearing();
    }
}
