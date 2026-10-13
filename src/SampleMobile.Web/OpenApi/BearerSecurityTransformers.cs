using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SampleMobile.Web.OpenApi;

/// <summary>Declares the bearer scheme the mobile API uses.</summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer {
    public const string SchemeName = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken) {
        document.Info.Title = "SampleMobile mobile API";
        document.Info.Description =
            "Provider-neutral notification inbox and device registration API for the MAUI client. " +
            "Obtain a token from POST /api/mobile/auth/login and send it as 'Authorization: Bearer {token}'.";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            In = ParameterLocation.Header,
            BearerFormat = "JWT",
            Description = "Access token from POST /api/mobile/auth/login."
        };

        return Task.CompletedTask;
    }
}

/// <summary>Marks every operation except anonymous ones (login) as requiring the bearer token.</summary>
internal sealed class BearerSecurityRequirementTransformer : IOpenApiOperationTransformer {
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken) {
        var allowsAnonymous = context.Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any();
        if (allowsAnonymous) {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement {
            [new OpenApiSecuritySchemeReference(BearerSecuritySchemeTransformer.SchemeName, context.Document)] = []
        });

        return Task.CompletedTask;
    }
}
