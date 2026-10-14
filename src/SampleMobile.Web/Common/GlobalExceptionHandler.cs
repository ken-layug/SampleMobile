using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace SampleMobile.Web.Common;

/// <summary>
/// Turns unhandled exceptions on API routes into a generic 500 problem response. Details stay in the
/// log (with the trace id); nothing about the exception is returned to the client.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler {

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken) {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested) {
            // The client went away; there is nobody to answer.
            return true;
        }

        logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred."
            }
        });
    }
}
