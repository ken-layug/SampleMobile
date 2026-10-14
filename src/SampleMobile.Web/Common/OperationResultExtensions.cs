using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SampleMobile.Application.Common;

namespace SampleMobile.Web.Common;

/// <summary>The single place business outcomes become HTTP status codes and RFC 9457 problem details.</summary>
public static class OperationResultExtensions {
    public static ActionResult ToActionResult(this OperationResult result, ControllerBase controller) {
        var detail = result.Errors.Count > 0
            ? string.Join("; ", result.Errors.Select(error => error.Description))
            : null;

        return result.Status switch {
            OperationStatus.Unauthorized => controller.Problem(detail: detail, statusCode: StatusCodes.Status401Unauthorized),
            OperationStatus.NotFound => controller.Problem(detail: detail, statusCode: StatusCodes.Status404NotFound),
            OperationStatus.Conflict => controller.Problem(detail: detail, statusCode: StatusCodes.Status409Conflict),
            OperationStatus.Invalid => controller.ValidationProblem(ToModelState(result.Errors)),
            _ => controller.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest)
        };
    }

    // Error codes are property names, so handler validation produces the same shape as model binding.
    private static ModelStateDictionary ToModelState(IReadOnlyList<OperationError> errors) {
        var modelState = new ModelStateDictionary();
        foreach (var error in errors) {
            modelState.AddModelError(error.Code, error.Description);
        }

        return modelState;
    }
}
