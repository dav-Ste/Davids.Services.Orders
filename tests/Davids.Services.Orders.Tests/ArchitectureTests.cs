using Davids.Services.Orders.Application.Services;
using Davids.Services.Orders.Core.Entities;
using Davids.Services.Orders.Infrastructure;
using Davids.Services.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Davids.Services.Orders.Tests;

[Trait("Category", "Unit")]
public sealed class ArchitectureTests
{
    [Fact]
    public void CoreLayerHasZeroOuterLayerOrInfrastructureFrameworkDependencies()
    {
        var coreRefs = typeof(Order).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(coreRefs, n => n.StartsWith("Davids.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(coreRefs, n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(coreRefs, n => n.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(coreRefs, n => n.StartsWith("RabbitMQ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplicationLayerDependsOnlyOnCoreAndNeverOnInfrastructureOrApi()
    {
        var appRefs = typeof(OrdersService).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        Assert.Contains("Davids.Services.Orders.Core", appRefs, StringComparer.Ordinal);
        Assert.DoesNotContain("Davids.Services.Orders.Infrastructure", appRefs, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Davids.Services.Orders.Api", appRefs, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Davids.ServiceDefaults", appRefs, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(appRefs, n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(appRefs, n => n.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InfrastructureAndApiLayersFollowInwardDependenciesAndNeverCoupleToSiblingServices()
    {
        var infraRefs = typeof(OrdersDbContext).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("Davids.Services.Orders.Api", infraRefs, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(infraRefs, n => n.StartsWith("Davids.Services.Products", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(infraRefs, n => n.StartsWith("Davids.Services.Baskets", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(infraRefs, n => n.StartsWith("Davids.Services.Payments", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(infraRefs, n => n.StartsWith("Davids.Services.Customers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EfCoreRuntimeModelMatchesMigrationsSnapshotWithoutPendingChanges()
    {
        using var designTimeDb = new DesignTimeFactory().CreateDbContext([]);
        Assert.False(designTimeDb.Database.HasPendingModelChanges());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ordersdb"] = "Server=localhost;Database=davids_orders;Integrated Security=true;TrustServerCertificate=true",
                ["CustomerOidc:Authority"] = "https://localhost:7504",
                ["AdminOidc:Authority"] = "https://localhost:17325",
                ["Messaging:Enabled"] = "false"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddOrdersInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var runtimeDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        Assert.False(runtimeDb.Database.HasPendingModelChanges());
    }
}
