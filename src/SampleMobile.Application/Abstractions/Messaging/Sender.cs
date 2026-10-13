using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace SampleMobile.Application.Abstractions.Messaging;

/// <summary>
/// In-process request dispatcher. The compile-time type at a call site is <c>IRequest&lt;TResult&gt;</c>,
/// so the handler's closed interface is recovered from the request's runtime type via a generic wrapper
/// that is created once per request type and cached for the process lifetime — no per-call reflection.
/// Registered scoped so handlers resolve their scoped dependencies from the current request scope.
/// </summary>
public sealed class Sender(IServiceProvider services) : ISender {
    private static readonly ConcurrentDictionary<Type, object> Wrappers = new();

    public Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);
        var wrapper = (RequestHandlerWrapper<TResult>)Wrappers.GetOrAdd(
            request.GetType(),
            static requestType => Activator.CreateInstance(
                typeof(RequestHandlerWrapper<,>).MakeGenericType(requestType, typeof(TResult)))!);
        return wrapper.HandleAsync(request, services, cancellationToken);
    }

    private abstract class RequestHandlerWrapper<TResult> {
        public abstract Task<TResult> HandleAsync(
            IRequest<TResult> request, IServiceProvider services, CancellationToken cancellationToken);
    }

    private sealed class RequestHandlerWrapper<TRequest, TResult> : RequestHandlerWrapper<TResult>
        where TRequest : IRequest<TResult> {
        public override Task<TResult> HandleAsync(
            IRequest<TResult> request, IServiceProvider services, CancellationToken cancellationToken) =>
            services.GetRequiredService<IRequestHandler<TRequest, TResult>>()
                .HandleAsync((TRequest)request, cancellationToken);
    }
}
