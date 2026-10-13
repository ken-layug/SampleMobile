using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Contracts;
using SampleMobile.Application.Features.Devices;
using SampleMobile.Web.Common;

namespace SampleMobile.Web.Controllers.Mobile;

[ApiController]
[Route("api/mobile/device-registrations")]
[Authorize(Policy = AuthorizationPolicies.MobileApi)]
[Produces("application/json")]
[Tags("Device registrations")]
public sealed class DeviceRegistrationsController(ISender sender) : ControllerBase {
    /// <summary>Registers this installation for push, or refreshes an existing registration.</summary>
    /// <remarks>
    /// Idempotent per (installationId, provider): call it on every app start and whenever the provider
    /// rotates the token. The owner is always the signed-in user; if another user previously registered
    /// the same installation, the registration moves to the caller.
    /// </remarks>
    /// <response code="201">A new registration was created.</response>
    /// <response code="200">An existing registration was refreshed.</response>
    [HttpPost]
    [ProducesResponseType<DeviceRegistrationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<DeviceRegistrationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<DeviceRegistrationResponse>> Register(
        RegisterDeviceRequest request, CancellationToken cancellationToken) {
        var result = await sender.SendAsync(
            new RegisterDeviceCommand(User.GetUserId(), request.InstallationId!, request.Platform!, request.Provider!, request.Address!),
            cancellationToken);

        if (!result.Succeeded) {
            return result.ToActionResult(this);
        }

        return result.Value!.Created
            ? CreatedAtAction(nameof(List), routeValues: null, result.Value.Registration)
            : Ok(result.Value.Registration);
    }

    /// <summary>Lists the signed-in user's registrations, including disabled ones. Addresses are never returned.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DeviceRegistrationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<DeviceRegistrationResponse>>> List(CancellationToken cancellationToken) {
        var result = await sender.SendAsync(new ListMyDevicesQuery(User.GetUserId()), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToActionResult(this);
    }

    /// <summary>Stops push to this installation for the given provider (call on logout).</summary>
    /// <remarks>Idempotent. Only the caller's own registration can be removed; anything else is reported as 404.</remarks>
    /// <param name="installationId">The installation id used when registering.</param>
    /// <param name="provider">The provider id used when registering, e.g. <c>fcm</c>.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpDelete("{installationId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Unregister(
        [MaxLength(128)] string installationId,
        [FromQuery, Required, MaxLength(50)] string? provider,
        CancellationToken cancellationToken) {
        var result = await sender.SendAsync(
            new UnregisterDeviceCommand(User.GetUserId(), installationId, provider!), cancellationToken);
        return result.Succeeded ? NoContent() : result.ToActionResult(this);
    }
}
