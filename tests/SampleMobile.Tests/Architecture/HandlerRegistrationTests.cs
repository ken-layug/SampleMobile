using Microsoft.Extensions.DependencyInjection;
using SampleMobile.Application;
using SampleMobile.Application.Abstractions.Events;
using SampleMobile.Application.Abstractions.Messaging;

namespace SampleMobile.Tests.Architecture;

/// <summary>
/// Handlers are registered by convention (request handlers by scan, event handlers by explicit list).
/// Both conventions fail silently at runtime, so these tests make them fail at build time instead.
/// </summary>
public class HandlerRegistrationTests {
    [Fact]
    public void Every_request_in_the_application_assembly_has_exactly_one_registered_handler() {
        var services = new ServiceCollection();
        services.AddApplication();

        var unhandled = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Select(type => (Type: type, RequestInterface: type.GetInterfaces()
                .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IRequest<>))))
            .Where(pair => pair.RequestInterface is not null)
            .Select(pair => (pair.Type, HandlerType: typeof(IRequestHandler<,>)
                .MakeGenericType(pair.Type, pair.RequestInterface!.GetGenericArguments()[0])))
            .Where(pair => services.Count(descriptor => descriptor.ServiceType == pair.HandlerType) != 1)
            .Select(pair => pair.Type.Name)
            .ToList();

        Assert.True(unhandled.Count == 0, $"Requests without exactly one handler: {string.Join(", ", unhandled)}");
    }

    [Fact]
    public void Every_domain_event_handler_is_registered_for_each_event_it_handles() {
        var services = new ServiceCollection();
        services.AddApplication();

        var implementations = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetInterfaces()
                .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>))
                .Select(handlerInterface => (Type: type, HandlerInterface: handlerInterface)))
            .ToList();
        Assert.NotEmpty(implementations);

        var unregistered = implementations
            .Where(pair => !services.Any(descriptor => descriptor.ServiceType == pair.HandlerInterface && descriptor.ImplementationType == pair.Type))
            .Select(pair => $"{pair.Type.Name} -> {pair.HandlerInterface.GetGenericArguments()[0].Name}")
            .ToList();

        Assert.True(unregistered.Count == 0, $"Domain event handlers missing a DI registration: {string.Join(", ", unregistered)}");
    }
}
