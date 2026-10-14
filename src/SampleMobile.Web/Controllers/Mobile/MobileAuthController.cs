using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Contracts;
using SampleMobile.Application.Features.Auth;
using SampleMobile.Web.Common;

namespace SampleMobile.Web.Controllers.Mobile;

[ApiController]
[Route("api/mobile/auth")]
[Produces("application/json")]
[Tags("Mobile authentication")]
public sealed class MobileAuthController(ISender sender) : ControllerBase {
    /// <summary>Signs in with a user name and password and returns a bearer token.</summary>
    /// <remarks>The only anonymous mobile endpoint. Repeated failures lock the account for a few minutes.</remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken) {
        var result = await sender.SendAsync(new LoginCommand(request.UserName!, request.Password!), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToActionResult(this);
    }
}
