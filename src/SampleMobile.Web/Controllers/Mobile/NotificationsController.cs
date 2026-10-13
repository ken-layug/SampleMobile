using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Common;
using SampleMobile.Application.Contracts;
using SampleMobile.Application.Features.Notifications;
using SampleMobile.Web.Common;

namespace SampleMobile.Web.Controllers.Mobile;

/// <summary>
/// The signed-in user's notification inbox. It is served from PostgreSQL and works whether or not any
/// push provider is configured or reachable.
/// </summary>
[ApiController]
[Route("api/mobile/notifications")]
[Authorize(Policy = AuthorizationPolicies.MobileApi)]
[Produces("application/json")]
[Tags("Notification inbox")]
public sealed class NotificationsController(ISender sender) : ControllerBase {
    /// <summary>Lists the signed-in user's notifications, newest first.</summary>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Items per page, 1-100.</param>
    /// <param name="unreadOnly">Only return unread notifications.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [ProducesResponseType<NotificationListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<NotificationListResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize,
        [FromQuery] bool unreadOnly = false,
        CancellationToken cancellationToken = default) {
        var result = await sender.SendAsync(new GetInboxQuery(User.GetUserId(), page, pageSize, unreadOnly), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToActionResult(this);
    }

    /// <summary>Gets one notification addressed to the signed-in user.</summary>
    /// <remarks>Fetch this when a push arrives: the push payload only carries the id.</remarks>
    [HttpGet("{notificationId:guid}")]
    [ProducesResponseType<NotificationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<NotificationResponse>> Get(Guid notificationId, CancellationToken cancellationToken) {
        var result = await sender.SendAsync(new GetInboxNotificationQuery(User.GetUserId(), notificationId), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToActionResult(this);
    }

    /// <summary>Marks a notification read for the signed-in user.</summary>
    /// <remarks>Idempotent: repeating the call keeps the original <c>readAt</c>.</remarks>
    [HttpPost("{notificationId:guid}/read")]
    [ProducesResponseType<NotificationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<NotificationResponse>> MarkRead(Guid notificationId, CancellationToken cancellationToken) {
        var result = await sender.SendAsync(new MarkNotificationReadCommand(User.GetUserId(), notificationId), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToActionResult(this);
    }
}
