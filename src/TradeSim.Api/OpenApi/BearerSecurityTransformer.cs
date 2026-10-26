using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TradeSim.Api.OpenApi;

internal sealed class BearerSecurityTransformer : IOpenApiDocumentTransformer
{
    private const string SchemeName = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "TradeSim API";
        document.Info.Description = "Stock trading simulation. Log in with POST /api/v1/auth/login (e.g. trader1 / Password123!), " +
                                    "then click Authorize and paste the accessToken.";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "The accessToken from POST /api/v1/auth/login.",
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeName, document)] = [],
        });

        return Task.CompletedTask;
    }
}
