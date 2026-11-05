using Davids.Services.Orders.Application.Services;
using Davids.Services.Orders.Core.Repositories;
using Davids.Services.Orders.Infrastructure.Auth;
using Davids.Services.Orders.Infrastructure.Clients;
using Davids.Services.Orders.Infrastructure.Messaging;
using Davids.Services.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Davids.Services.Orders.Infrastructure;

public static class OrdersInfrastructureExtensions
{
    public const string CustomerScheme = "CustomerBearer";
    public const string AdminScheme = "AdminBearer";

    public static IServiceCollection AddOrdersInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var customerOidc = configuration.GetSection("CustomerOidc").Get<OidcAuthoritySettings>()
            ?? new OidcAuthoritySettings { Audience = "davids-services" };
        customerOidc.Audience ??= "davids-services";
        customerOidc.ValidateAndResolveStaticPublicKey("CustomerOidc");
        services.AddSingleton(customerOidc);

        var adminOidc = configuration.GetSection("AdminOidc").Get<OidcAuthoritySettings>()
            ?? new OidcAuthoritySettings { Audience = "davids-admin" };
        adminOidc.Audience ??= "davids-admin";
        adminOidc.ValidateAndResolveStaticPublicKey("AdminOidc");

        var connectionString = configuration.GetConnectionString("ordersdb")
            ?? throw new InvalidOperationException("Missing ordersdb connection.");
        services.AddDbContext<OrdersDbContext>(options => options.UseSqlServer(connectionString));

        var basketsBaseUrl = configuration["BasketsApi:BaseUrl"] ?? "http://localhost:5008/";
        services.AddHttpClient<IBasketCheckoutClient, HttpBasketCheckoutClient>(client =>
        {
            client.BaseAddress = new Uri(basketsBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        var productsBaseUrl = configuration["ProductsApi:BaseUrl"] ?? "http://localhost:5006/";
        services.AddHttpClient<IProductCatalogVerifier, HttpProductCatalogVerifier>(client =>
        {
            client.BaseAddress = new Uri(productsBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IOrdersService, OrdersService>();

        if (configuration.GetValue("Messaging:Enabled", true))
        {
            services.AddHostedService<OrdersMessageWorker>();
        }

        services.AddHealthChecks().AddCheck<OrdersDatabaseHealthCheck>("orders-database");
        return services;
    }
}
