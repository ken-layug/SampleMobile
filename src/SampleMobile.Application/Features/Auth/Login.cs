using SampleMobile.Application.Abstractions.Identity;
using SampleMobile.Application.Abstractions.Messaging;
using SampleMobile.Application.Common;
using SampleMobile.Application.Contracts;

namespace SampleMobile.Application.Features.Auth;

public sealed record LoginCommand(string UserName, string Password) : ICommand<OperationResult<LoginResponse>>;

public sealed class LoginCommandHandler(
    IIdentityService identity,
    IAccessTokenIssuer tokenIssuer,
    TimeProvider timeProvider) : ICommandHandler<LoginCommand, OperationResult<LoginResponse>> {

    public async Task<OperationResult<LoginResponse>> HandleAsync(
        LoginCommand command, CancellationToken cancellationToken = default) {
        var user = await identity.ValidateCredentialsAsync(command.UserName, command.Password, cancellationToken);
        if (user is null) {
            // One message for unknown user, wrong password and lockout, so the endpoint can't be used to probe accounts.
            return OperationResult<LoginResponse>.Unauthorized("Invalid user name or password.");
        }

        var token = tokenIssuer.Issue(user);
        var expiresIn = (int)Math.Max(0, (token.ExpiresAt - timeProvider.GetUtcNow()).TotalSeconds);
        return OperationResult<LoginResponse>.Success(new LoginResponse(token.Value, "Bearer", token.ExpiresAt, expiresIn));
    }
}
