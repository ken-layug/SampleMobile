using Microsoft.AspNetCore.Components;
using SampleMobile.Application.Common;

namespace SampleMobile.Web.Components.Shared;

/// <summary>Outcome of a dashboard action, shown as a success or error alert.</summary>
public sealed record ActionResultMessage(
    bool Succeeded,
    string Message,
    IReadOnlyList<string> Details,
    string? LinkHref = null,
    string? LinkText = null) {
    public static ActionResultMessage Success(string message, string? linkHref = null, string? linkText = null) =>
        new(true, message, [], linkHref, linkText);

    public static ActionResultMessage Failure(OperationResult result) =>
        new(false, "The request was rejected.", result.Errors.Select(error => error.Description).ToList());
}

public partial class ResultAlert {
    [Parameter]
    public ActionResultMessage? Result { get; set; }
}
