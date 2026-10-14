using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SampleMobile.Mobile.Core.Api;
using SampleMobile.Mobile.Core.Push;
using SampleMobile.Mobile.Core.Session;

namespace SampleMobile.Mobile.Core.ViewModels;

/// <summary>Login screen: backend URL (development), credentials and the push mode used for this sign-in.</summary>
public sealed partial class LoginViewModel : ObservableObject {
    private readonly ISessionService _session;
    private readonly IBackendSettings _backend;
    private readonly PushTransportSelector _transports;

    public LoginViewModel(ISessionService session, IBackendSettings backend, PushTransportSelector transports) {
        _session = session;
        _backend = backend;
        _transports = transports;

        BackendUrl = backend.BaseUri?.ToString() ?? string.Empty;
        UserName = string.Empty;
        Password = string.Empty;
        UseInboxOnly = transports.Mode == PushMode.InboxOnly;
        HasRealPushTransport = transports.Preferred is not null;
        RealPushTransportName = transports.Preferred?.DisplayName ?? "none";
    }

    [ObservableProperty]
    public partial string BackendUrl { get; set; }

    [ObservableProperty]
    public partial string UserName { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; }

    /// <summary>Development switch: skip real push and keep the inbox in sync through the API only.</summary>
    [ObservableProperty]
    public partial bool UseInboxOnly { get; set; }

    public bool HasRealPushTransport { get; }

    public string RealPushTransportName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Why the user is here, for example "Your session expired".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    public partial bool IsBusy { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync() {
        ErrorMessage = null;
        var urlError = _backend.TrySetBaseUrl(BackendUrl);
        if (urlError is not null) {
            ErrorMessage = urlError;
            return;
        }

        BackendUrl = _backend.BaseUri!.ToString();
        _transports.Mode = UseInboxOnly ? PushMode.InboxOnly : PushMode.Automatic;

        IsBusy = true;
        try {
            var result = await _session.LoginAsync(UserName, Password);
            if (result.Succeeded) {
                StatusMessage = null;
            } else {
                ErrorMessage = result.Error;
            }
        } finally {
            Password = string.Empty;
            IsBusy = false;
        }
    }

    private bool CanLogin() => !IsBusy;
}
