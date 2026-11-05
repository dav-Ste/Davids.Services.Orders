using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Davids.Services.Orders.Application.DTO;
using Davids.Services.Orders.Application.Services;

namespace Davids.Services.Orders.Infrastructure.Clients;

public sealed class HttpBasketCheckoutClient(HttpClient httpClient) : IBasketCheckoutClient
{
    private sealed record RemoteBasketItem(Guid ProductId, int Quantity);
    private sealed record RemoteBasket(IReadOnlyList<RemoteBasketItem> Items);

    public async Task<IReadOnlyList<BasketSnapshotLine>> GetCustomerBasketLinesAsync(
        Guid customerId,
        string? bearerToken,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "baskets/me");
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        var basket = await response.Content.ReadFromJsonAsync<RemoteBasket>(cancellationToken: ct);
        return basket?.Items?.Select(i => new BasketSnapshotLine(i.ProductId, i.Quantity)).ToArray() ?? [];
    }

    public async Task ClearCustomerBasketAsync(Guid customerId, string? bearerToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "baskets/me");
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }
}

public sealed class HttpProductCatalogVerifier(HttpClient httpClient) : IProductCatalogVerifier
{
    public async Task<VerifiedProductSnapshot?> GetProductAsync(Guid productId, CancellationToken ct = default)
    {
        using var response = await httpClient.GetAsync($"products/{productId}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<VerifiedProductSnapshot>(cancellationToken: ct);
    }
}
