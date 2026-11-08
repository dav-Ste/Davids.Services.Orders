using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Davids.Services.Orders.Api;

public static class OrdersOpenApiExtensions
{
    private static JsonNode[] AllowedPaymentMethods =>
    [
        JsonValue.Create("card"),
        JsonValue.Create("wallet"),
        JsonValue.Create("invoice")
    ];

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

                if (document.Components.Schemas is not null &&
                    document.Components.Schemas.TryGetValue("CheckoutOrder", out var checkoutRef) &&
                    checkoutRef is OpenApiSchema checkoutSchema &&
                    checkoutSchema.Properties is not null)
                {
                    checkoutSchema.Required = new HashSet<string>(StringComparer.Ordinal) { "shippingFullName", "shippingAddress" };
                    ConfigureNonEmptyString(checkoutSchema.Properties, "shippingFullName", 2, 120);
                    ConfigureNonEmptyString(checkoutSchema.Properties, "shippingAddress", 5, 300);

                    if (checkoutSchema.Properties.TryGetValue("paymentMethod", out var methodProp) && methodProp is OpenApiSchema methodSchema)
                    {
                        methodSchema.Type = JsonSchemaType.String | JsonSchemaType.Null;
                        methodSchema.Enum = [(JsonNode?)null!, .. AllowedPaymentMethods];
                    }

                    if (checkoutSchema.Properties.TryGetValue("cardLast4", out var last4Prop) && last4Prop is OpenApiSchema last4Schema)
                    {
                        last4Schema.Type = JsonSchemaType.String | JsonSchemaType.Null;
                        last4Schema.MinLength = 4;
                        last4Schema.MaxLength = 4;
                        last4Schema.Pattern = @"^[0-9]{4}$";
                    }
                }

                foreach (var (pathKey, pathItem) in document.Paths ?? [])
                {
                    foreach (var (_, operation) in pathItem.Operations ?? [])
                    {
                        if (operation.RequestBody is OpenApiRequestBody reqBody)
                        {
                            reqBody.Required = true;
                        }

                        foreach (var parameter in operation.Parameters ?? [])
                        {
                            if (string.Equals(parameter.Name, "limit", StringComparison.Ordinal) &&
                                parameter.Schema is OpenApiSchema limitSchema)
                            {
                                limitSchema.Type = JsonSchemaType.Integer;
                                limitSchema.Minimum = "1";
                                limitSchema.Maximum = string.Equals(pathKey, "/orders/me", StringComparison.Ordinal) ? "200" : "500";
                            }
                            else if (string.Equals(parameter.Name, "search", StringComparison.Ordinal) &&
                                     parameter.Schema is OpenApiSchema searchSchema)
                            {
                                searchSchema.MaxLength = 100;
                                searchSchema.Pattern = @"^[^\u0000]*$";
                            }
                            else if (string.Equals(parameter.Name, "status", StringComparison.Ordinal) &&
                                     parameter.Schema is OpenApiSchema statusSchema)
                            {
                                statusSchema.MaxLength = 40;
                                statusSchema.Pattern = @"^[^\u0000]*$";
                            }
                        }

                        if (operation.Security is { Count: > 0 })
                        {
                            operation.Security =
                            [
                                new OpenApiSecurityRequirement
                                {
                                    [new OpenApiSecuritySchemeReference("BearerAuth", document)] = []
                                }
                            ];
                        }
                    }
                }

                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                var requiresAuth = metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any();

                operation.Responses ??= new OpenApiResponses();
                operation.Responses.TryAdd("429", new OpenApiResponse { Description = "Rate limit exceeded." });

                if (requiresAuth)
                {
                    operation.Security = [new OpenApiSecurityRequirement()];
                    operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing or invalid RS256 Bearer access token." });
                    operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Caller lacks permission for this order resource." });
                }

                return Task.CompletedTask;
            });
        });
        return services;
    }

    private static void ConfigureNonEmptyString(IDictionary<string, IOpenApiSchema> properties, string name, int minLength, int maxLength)
    {
        if (properties.TryGetValue(name, out var prop) && prop is OpenApiSchema schema)
        {
            schema.Type = JsonSchemaType.String;
            schema.MinLength = minLength;
            schema.MaxLength = maxLength;
            schema.Pattern = @"^[^\u0000]*\S[^\u0000]*\S[^\u0000]*$";
        }
    }
}
