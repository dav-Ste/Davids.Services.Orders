using System.Security.Claims;
using Davids.ServiceDefaults;
using Davids.Services.Orders.Api;
using Davids.Services.Orders.Api.Exceptions;
using Davids.Services.Orders.Application.Commands;
using Davids.Services.Orders.Application.Services;
using Davids.Services.Orders.Infrastructure;
using Davids.Services.Orders.Infrastructure.Auth;
using Davids.Services.Orders.Infrastructure.Persistence;
using Davids.Services.Orders.Infrastructure.Secrets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
await builder.Configuration.LoadVaultAsync();
builder.AddServiceDefaults("davids-orders");
builder.Services.AddTrustedForwardedHeaders(builder.Configuration, builder.Environment);
builder.Services.AddOrdersInfrastructure(builder.Configuration);

var customerOidc = builder.Configuration.GetSection("CustomerOidc").Get<OidcAuthoritySettings>()
    ?? new OidcAuthoritySettings { Audience = "davids-services" };
customerOidc.Audience ??= "davids-services";
var customerStaticKey = customerOidc.ValidateAndResolveStaticPublicKey("CustomerOidc");

var adminOidc = builder.Configuration.GetSection("AdminOidc").Get<OidcAuthoritySettings>()
    ?? new OidcAuthoritySettings { Audience = "davids-admin" };
adminOidc.Audience ??= "davids-admin";
var adminStaticKey = adminOidc.ValidateAndResolveStaticPublicKey("AdminOidc");

var requireHttpsMetadata =
    builder.Environment.IsProduction() ||
    !(builder.Environment.IsDevelopment() ||
      builder.Environment.IsEnvironment("Testing") ||
      builder.Configuration.GetValue<bool>("Http:AllowInsecureDevelopment"));

builder.Services.AddAuthentication(OrdersInfrastructureExtensions.CustomerScheme)
    .AddJwtBearer(OrdersInfrastructureExtensions.CustomerScheme, options =>
    {
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = requireHttpsMetadata;
        if (!string.IsNullOrWhiteSpace(customerOidc.MetadataAddress))
        {
            options.MetadataAddress = customerOidc.MetadataAddress;
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(customerOidc.EffectiveIssuer),
            ValidIssuer = customerOidc.EffectiveIssuer,
            ValidateAudience = true,
            ValidAudience = customerOidc.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = customerStaticKey,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var role = context.Principal?.FindFirstValue("role");
                if (!string.Equals(role, DavidsRoles.Customer, StringComparison.Ordinal))
                {
                    context.Fail("CustomerBearer accepts customer ('user') tokens only.");
                }

                return Task.CompletedTask;
            }
        };
    })
    .AddJwtBearer(OrdersInfrastructureExtensions.AdminScheme, options =>
    {
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = requireHttpsMetadata;
        if (!string.IsNullOrWhiteSpace(adminOidc.MetadataAddress))
        {
            options.MetadataAddress = adminOidc.MetadataAddress;
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(adminOidc.EffectiveIssuer),
            ValidIssuer = adminOidc.EffectiveIssuer,
            ValidateAudience = true,
            ValidAudience = adminOidc.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = adminStaticKey,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var role = context.Principal?.FindFirstValue("role");
                if (!DavidsRoles.CanReadWorkforce(role))
                {
                    context.Fail("AdminBearer accepts workforce roles (admin, support, auditor) only.");
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CustomerOnly", policy =>
    {
        policy.AddAuthenticationSchemes(OrdersInfrastructureExtensions.CustomerScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("role", DavidsRoles.Customer);
    })
    .AddPolicy("WorkforceRead", policy =>
    {
        policy.AddAuthenticationSchemes(OrdersInfrastructureExtensions.AdminScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("role", DavidsRoles.Admin, DavidsRoles.Support, DavidsRoles.Auditor);
    });

builder.Services.AddExceptionHandler<OrderApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddOrdersOpenApi();

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Automatic migration is development-only.");
    }

    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.MigrateAsync();
}

app.UseTrustedForwardedHeaders();
app.UseExceptionHandler();
if (!(app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Http:AllowInsecureDevelopment")))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "docs";
        options.SwaggerEndpoint("../openapi/v1.json", "Davids Orders v1");
    });
}

static bool TryGetCustomerId(ClaimsPrincipal user, out Guid customerId)
{
    var raw = user.FindFirstValue("sub");
    return Guid.TryParse(raw, out customerId) && customerId != Guid.Empty;
}

static string? ExtractRawBearerToken(HttpContext context)
{
    var header = context.Request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? header["Bearer ".Length..].Trim()
        : null;
}

app.MapGet("/", () => Results.Text("Davids Orders Service"))
    .ExcludeFromDescription()
    .AllowAnonymous();

app.MapPost("/orders/checkout", async (CheckoutOrder command, ClaimsPrincipal user, HttpContext ctx, IOrdersService service, CancellationToken ct) =>
    {
        if (!TryGetCustomerId(user, out var customerId))
        {
            return Results.Unauthorized();
        }

        var email = user.FindFirstValue("email") ?? ctx.Request.Headers["X-Auth-User-Email"].FirstOrDefault();
        var rawToken = ExtractRawBearerToken(ctx);
        var created = await service.CheckoutAsync(customerId, email, rawToken, command, ct);
        return Results.Created($"/orders/me/{created.Id}", created);
    })
    .RequireAuthorization("CustomerOnly");

app.MapGet("/orders/me", async (int? limit, ClaimsPrincipal user, IOrdersService service, CancellationToken ct) =>
    {
        if (!TryGetCustomerId(user, out var customerId))
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await service.GetCustomerOrdersAsync(customerId, limit ?? 50, ct));
    })
    .RequireAuthorization("CustomerOnly");

app.MapGet("/orders/me/{orderId:guid}", async (Guid orderId, ClaimsPrincipal user, IOrdersService service, CancellationToken ct) =>
    {
        if (!TryGetCustomerId(user, out var customerId))
        {
            return Results.Unauthorized();
        }

        var order = await service.GetCustomerOrderAsync(customerId, orderId, ct);
        return order is null ? Results.NotFound() : Results.Ok(order);
    })
    .RequireAuthorization("CustomerOnly");

app.MapPost("/orders/me/{orderId:guid}/cancel", async (Guid orderId, ClaimsPrincipal user, IOrdersService service, CancellationToken ct) =>
    {
        if (!TryGetCustomerId(user, out var customerId))
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await service.CancelCustomerOrderAsync(customerId, orderId, ct));
    })
    .RequireAuthorization("CustomerOnly");

app.MapGet("/orders", async (string? search, string? status, Guid? customerId, int? limit, IOrdersService service, CancellationToken ct) =>
    {
        return Results.Ok(await service.SearchWorkforceAsync(search, status, customerId, limit ?? 100, ct));
    })
    .RequireAuthorization("WorkforceRead");

app.MapGet("/orders/{orderId:guid}", async (Guid orderId, IOrdersService service, CancellationToken ct) =>
    {
        var order = await service.GetWorkforceOrderAsync(orderId, ct);
        return order is null ? Results.NotFound() : Results.Ok(order);
    })
    .RequireAuthorization("WorkforceRead");

app.Run();

public partial class Program;
