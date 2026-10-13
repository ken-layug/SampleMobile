namespace SampleMobile.Application.Abstractions.Messaging;

/// <summary>A dispatchable request producing <typeparamref name="TResult"/>. Dispatch via <see cref="ISender"/>.</summary>
/// <typeparam name="TResult">The result the request's handler produces.</typeparam>
public interface IRequest<TResult>;

/// <summary>A request that changes state.</summary>
public interface ICommand<TResult> : IRequest<TResult>;

/// <summary>A side-effect-free read.</summary>
public interface IQuery<TResult> : IRequest<TResult>;
