using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Davids.Services.Orders.Api;

public static class OrdersOpenApiExtensions
{
    public static IServiceCollection AddOrdersOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Davids Orders API";
                document.Info.Version = "1.0.0";
                document.Info.Description = "Customer checkout, order history, and workforce order inspection endpoints.";

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["BearerAuth"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "RS256 OIDC access token (CustomerBearer or AdminBearer)."
                };
                return Task.CompletedTask;
            });
        });
        return services;
    }
}
