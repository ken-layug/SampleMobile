using SampleMobile.Application.Abstractions.Messaging;

namespace SampleMobile.Web.Components;

/// <summary>Sends application requests from interactive dashboard components.</summary>
public interface IDashboardSender {
    Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default);
}

/// <summary>
/// In Blazor Server a DI scope lives as long as the circuit, so injecting <see cref="ISender"/> directly
/// would share one DbContext across every click and auto-refresh tick — and fail as soon as two overlap.
/// Each request here gets its own scope, exactly like an HTTP request does.
/// </summary>
public sealed class DashboardSender(IServiceScopeFactory scopeFactory) : IDashboardSender {
    public async Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default) {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().SendAsync(request, cancellationToken);
    }
}
