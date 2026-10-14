using SampleMobile.Mobile.Core.ViewModels;

namespace SampleMobile.Mobile.Views;

/// <summary>Login screen.</summary>
public partial class LoginPage : ContentPage {
    private readonly LoginViewModel _viewModel;

    public LoginPage(LoginViewModel viewModel) {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    /// <summary>Explains why the user is back here (for example an expired session).</summary>
    public void ShowStatus(string? message) => _viewModel.StatusMessage = message;
}
