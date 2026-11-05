using Davids.Services.Orders.Core.Entities;

namespace Davids.Services.Orders.Core.Repositories;

public interface IOrderRepository
{
    Task<Order?> GetAsync(Guid orderId, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetByCustomerAsync(Guid customerId, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> SearchAsync(string? search, OrderStatus? status, Guid? customerId, int limit, CancellationToken ct = default);
    Task AddAsync(Order order, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
