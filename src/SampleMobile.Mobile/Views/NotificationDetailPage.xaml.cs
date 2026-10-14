using SampleMobile.Mobile.Core.ViewModels;

namespace SampleMobile.Mobile.Views;

/// <summary>One notification, opened from the inbox or from a notification tap (<c>notification?id=…</c>).</summary>
public partial class NotificationDetailPage : ContentPage, IQueryAttributable {
    private readonly NotificationDetailViewModel _viewModel;
    private bool _loaded;

    public NotificationDetailPage(NotificationDetailViewModel viewModel) {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query) {
        _viewModel.SetNotificationId(query.TryGetValue("id", out var id) ? id as string : null);
        _loaded = false;
    }

    protected override async void OnAppearing() {
        base.OnAppearing();
        if (!_loaded) {
            _loaded = true;
            await _viewModel.LoadAsync();
        }
    }
}
