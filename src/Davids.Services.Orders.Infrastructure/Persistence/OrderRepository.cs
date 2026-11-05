using Davids.Services.Orders.Core.Entities;
using Davids.Services.Orders.Core.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Davids.Services.Orders.Infrastructure.Persistence;

public sealed class OrderRepository(OrdersDbContext db) : IOrderRepository
{
    public Task<Order?> GetAsync(Guid orderId, CancellationToken ct = default)
        => db.Orders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == orderId, ct);

    public async Task<IReadOnlyList<Order>> GetByCustomerAsync(Guid customerId, int limit, CancellationToken ct = default)
        => await db.Orders
            .AsNoTracking()
            .Include(x => x.Items)
            .Where(x => x.CustomerId == customerId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Order>> SearchAsync(
        string? search,
        OrderStatus? status,
        Guid? customerId,
        int limit,
        CancellationToken ct = default)
    {
        var query = db.Orders.AsNoTracking().Include(x => x.Items).AsQueryable();
        if (customerId.HasValue)
        {
            query = query.Where(x => x.CustomerId == customerId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.ShippingFullName.ToLower().Contains(term) ||
                (x.CustomerEmail != null && x.CustomerEmail.ToLower().Contains(term)) ||
                (x.PaymentReference != null && x.PaymentReference.ToLower().Contains(term)) ||
                x.ShippingAddress.ToLower().Contains(term));
        }

        return await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public Task AddAsync(Order order, CancellationToken ct = default)
        => db.Orders.AddAsync(order, ct).AsTask();

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
