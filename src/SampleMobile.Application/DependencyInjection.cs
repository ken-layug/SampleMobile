using Microsoft.Extensions.DependencyInjection;
using SampleMobile.Application.Abstractions.Messaging;

namespace SampleMobile.Application;

public static class DependencyInjection {
    /// <summary>Registers the application-layer use cases. Ports are bound in Infrastructure.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services) {
        // CQRS dispatch: every concrete IRequestHandler<,> in this assembly is discovered by scan,
        // so adding a feature handler never requires touching this file. The one failure mode —
        // a request whose handler is missing or duplicated — is caught by HandlerRegistrationTests.
        services.AddScoped<ISender, Sender>();
        foreach (var type in typeof(DependencyInjection).Assembly.GetTypes()) {
            if (type is not { IsClass: true, IsAbstract: false }) {
                continue;
            }

            foreach (var handlerInterface in type.GetInterfaces()) {
                if (handlerInterface.IsGenericType
                    && handlerInterface.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)) {
                    services.AddScoped(handlerInterface, type);
                }
            }
        }

        return services;
    }
}
