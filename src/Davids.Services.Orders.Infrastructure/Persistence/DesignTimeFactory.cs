using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Davids.Services.Orders.Infrastructure.Persistence;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    public OrdersDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseSqlServer("Server=localhost;Database=ordersdb;TrustServerCertificate=True")
            .Options;
        return new OrdersDbContext(options);
    }
}
