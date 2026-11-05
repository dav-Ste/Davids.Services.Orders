using Davids.Services.Orders.Application.DTO;

namespace Davids.Services.Orders.Application.Services;

public interface IOutboxWriter
{
    void Enqueue<T>(string type, T message);
}

public interface IBasketCheckoutClient
{
    Task<IReadOnlyList<BasketSnapshotLine>> GetCustomerBasketLinesAsync(Guid customerId, string? bearerToken, CancellationToken ct = default);
    Task ClearCustomerBasketAsync(Guid customerId, string? bearerToken, CancellationToken ct = default);
}

public interface IProductCatalogVerifier
{
    Task<VerifiedProductSnapshot?> GetProductAsync(Guid productId, CancellationToken ct = default);
}
