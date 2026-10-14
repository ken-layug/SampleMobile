using SampleMobile.Mobile.Core.ViewModels;

namespace SampleMobile.Mobile.Views;

/// <summary>The inbox list.</summary>
public partial class InboxPage : ContentPage {
    private readonly InboxViewModel _viewModel;

    public InboxPage(InboxViewModel viewModel) {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing() {
        base.OnAppearing();
        _viewModel.Activate();
    }

    protected override void OnDisappearing() {
        _viewModel.Deactivate();
        base.OnDisappearing();
    }

    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) {
        if (e.CurrentSelection.FirstOrDefault() is not NotificationItemViewModel item) {
            return;
        }

        Notifications.SelectedItem = null;
        await _viewModel.OpenCommand.ExecuteAsync(item);
    }
}
