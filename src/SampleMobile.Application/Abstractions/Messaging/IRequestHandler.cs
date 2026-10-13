namespace SampleMobile.Application.Abstractions.Messaging;

/// <summary>Handles a single request type. Exactly one handler per request; resolved from DI by <see cref="Sender"/>.</summary>
public interface IRequestHandler<in TRequest, TResult> where TRequest : IRequest<TResult> {
    Task<TResult> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

public interface ICommandHandler<in TCommand, TResult> : IRequestHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>;

public interface IQueryHandler<in TQuery, TResult> : IRequestHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>;
