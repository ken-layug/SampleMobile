namespace SampleMobile.Application.Abstractions.Messaging;

/// <summary>Dispatches a command or query to its single registered handler.</summary>
public interface ISender {
    Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default);
}
