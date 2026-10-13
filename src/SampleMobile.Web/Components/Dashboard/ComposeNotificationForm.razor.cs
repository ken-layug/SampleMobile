using Microsoft.AspNetCore.Components;
using SampleMobile.Application.Abstractions.Identity;
using SampleMobile.Application.Contracts;

namespace SampleMobile.Web.Components.Dashboard;

/// <summary>
/// The compose form. The page owns users, targets and submission; this component owns only the form
/// state, and pre-selects every compatible target whenever a new target list arrives.
/// </summary>
public partial class ComposeNotificationForm {
    private ComposeNotificationModel _model = new();
    private IReadOnlyList<DeliveryTargetOption>? _lastTargets;

    [Parameter, EditorRequired]
    public IReadOnlyList<UserSummary> Users { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<string> NotificationTypes { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<DeliveryTargetOption> Targets { get; set; } = [];

    [Parameter]
    public bool IsSubmitting { get; set; }

    [Parameter]
    public EventCallback<Guid?> OnRecipientChanged { get; set; }

    [Parameter]
    public EventCallback<ComposeNotificationModel> OnSubmit { get; set; }

    protected override void OnInitialized() {
        _model.Type = NotificationTypes.FirstOrDefault() ?? string.Empty;
        _model.Title = "Test notification";
        _model.Body = "Hello from ASP.NET Core";
    }

    protected override void OnParametersSet() {
        if (ReferenceEquals(Targets, _lastTargets)) {
            return;
        }

        _lastTargets = Targets;
        _model.SelectedTargetIds.Clear();
        foreach (var target in Targets.Where(target => target.IsCompatible)) {
            _model.SelectedTargetIds.Add(target.RegistrationId);
        }
    }

    private Task RecipientChangedAsync() => OnRecipientChanged.InvokeAsync(_model.RecipientUserId);

    private void ToggleTarget(Guid registrationId) {
        if (!_model.SelectedTargetIds.Remove(registrationId)) {
            _model.SelectedTargetIds.Add(registrationId);
        }
    }

    private async Task SubmitAsync() {
        await OnSubmit.InvokeAsync(_model);
    }
}
