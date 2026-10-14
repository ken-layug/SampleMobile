using SampleMobile.Mobile.Core.ViewModels;

namespace SampleMobile.Mobile.Views;

/// <summary>Development diagnostics (Debug builds only; see <see cref="AppShell"/>).</summary>
public partial class DiagnosticsPage : ContentPage {
    private readonly DiagnosticsViewModel _viewModel;

    public DiagnosticsPage(DiagnosticsViewModel viewModel) {
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
