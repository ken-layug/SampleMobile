using SampleMobile.Mobile.Core.ViewModels;

namespace SampleMobile.Mobile.Views;

/// <summary>Shown while the stored session is restored; then replaced by the shell or the login page.</summary>
public partial class StartupPage : ContentPage {
    private readonly StartupViewModel _viewModel;

    public StartupPage(StartupViewModel viewModel) {
        InitializeComponent();
        _viewModel = viewModel;
    }

    protected override async void OnAppearing() {
        base.OnAppearing();
        await _viewModel.StartAsync();
    }
}
