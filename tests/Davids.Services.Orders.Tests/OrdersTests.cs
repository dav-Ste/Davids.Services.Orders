using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Davids.Services.Orders.Application.Commands;
using Davids.Services.Orders.Application.DTO;
using Davids.Services.Orders.Application.Events;
using Davids.Services.Orders.Application.Services;
using Davids.Services.Orders.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Davids.Services.Orders.Tests;

public sealed class FakeBasketClient : IBasketCheckoutClient
{
    public ConcurrentDictionary<Guid, IReadOnlyList<BasketSnapshotLine>> Baskets { get; } = new();

    public Task<IReadOnlyList<BasketSnapshotLine>> GetCustomerBasketLinesAsync(Guid customerId, string? bearerToken, CancellationToken ct = default)
        => Task.FromResult(Baskets.TryGetValue(customerId, out var lines) ? lines : (IReadOnlyList<BasketSnapshotLine>)[]);

    public Task ClearCustomerBasketAsync(Guid customerId, string? bearerToken, CancellationToken ct = default)
    {
        Baskets[customerId] = [];
        return Task.CompletedTask;
    }
}

public sealed class FakeCatalogVerifier : IProductCatalogVerifier
{
    public ConcurrentDictionary<Guid, VerifiedProductSnapshot> Products { get; } = new();

    public Task<VerifiedProductSnapshot?> GetProductAsync(Guid productId, CancellationToken ct = default)
        => Task.FromResult(Products.TryGetValue(productId, out var p) ? p : null);
}

public sealed class OrdersFactory : WebApplicationFactory<Program>
{
    private const string CustomerIssuer = "https://identity.davids.test/";
    private const string AdminIssuer = "https://admin-identity.davids.test/";

    private static readonly RSA CustomerRsa = RSA.Create(2048);
    private static readonly RSA AdminRsa = RSA.Create(2048);

    private static readonly string CustomerPublicKeyPem = CustomerRsa.ExportSubjectPublicKeyInfoPem();
    private static readonly string AdminPublicKeyPem = AdminRsa.ExportSubjectPublicKeyInfoPem();

    private static readonly RsaSecurityKey CustomerKey = CreateKey(CustomerRsa);
    private static readonly RsaSecurityKey AdminKey = CreateKey(AdminRsa);

    private readonly SqliteConnection connection = new("Data Source=:memory:");
    public FakeBasketClient BasketClient { get; } = new();
    public FakeCatalogVerifier Catalog { get; } = new();

    public OrdersFactory() => connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("CustomerOidc:Issuer", CustomerIssuer);
        builder.UseSetting("CustomerOidc:Audience", "davids-services");
        builder.UseSetting("CustomerOidc:PublicKeyPem", CustomerPublicKeyPem);
        builder.UseSetting("AdminOidc:Issuer", AdminIssuer);
        builder.UseSetting("AdminOidc:Audience", "davids-admin");
        builder.UseSetting("AdminOidc:PublicKeyPem", AdminPublicKeyPem);
        builder.UseSetting("Vault:Enabled", "false");
        builder.UseSetting("Messaging:Enabled", "false");
        builder.UseSetting("Database:ApplyMigrations", "false");
        builder.UseSetting("ConnectionStrings:ordersdb", "unused");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<OrdersDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<OrdersDbContext>>();
            services.RemoveAll<OrdersDbContext>();
            services.AddDbContext<OrdersDbContext>(options => options.UseSqlite(connection));

            services.RemoveAll<IBasketCheckoutClient>();
            services.AddSingleton<IBasketCheckoutClient>(BasketClient);

            services.RemoveAll<IProductCatalogVerifier>();
            services.AddSingleton<IProductCatalogVerifier>(Catalog);
        });
    }

    public async Task<HttpClient> CreateOrdersClientAsync()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.EnsureCreatedAsync();
        return client;
    }

    public static string CreateCustomerToken(Guid sub) => Mint(sub, "user", CustomerIssuer, "davids-services", CustomerKey);
    public static string CreateWorkforceToken(Guid sub, string role = "auditor") => Mint(sub, role, AdminIssuer, "davids-admin", AdminKey);

    private static string Mint(Guid sub, string role, string issuer, string audience, RsaSecurityKey key)
    {
        var now = DateTime.UtcNow;
        var jwt = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: [new Claim("sub", sub.ToString("N")), new Claim("role", role), new Claim("jti", Guid.NewGuid().ToString("N"))],
            notBefore: now.AddSeconds(-5),
            expires: now.AddMinutes(15),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static RsaSecurityKey CreateKey(RSA rsa)
    {
        var spki = rsa.ExportSubjectPublicKeyInfo();
        var kid = Base64UrlEncoder.Encode(SHA256.HashData(spki))[..16];
        return new RsaSecurityKey(rsa.ExportParameters(true)) { KeyId = kid };
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            connection.Dispose();
        }
    }
}

[Trait("Category", "Integration")]
public sealed class OrdersTests
{
    [Fact]
    public async Task CheckoutCreatesPendingOrderWritesOutboxCompletesOnPaymentCapturedAndBlocksBola()
    {
        await using var factory = new OrdersFactory();
        var productId = Guid.NewGuid();
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();

        factory.Catalog.Products[productId] = new VerifiedProductSnapshot(productId, "DEV-KB-75PRO", "Mechanical Keyboard", 149.00m, "GBP", 10, true);
        factory.BasketClient.Baskets[customerA] = [new BasketSnapshotLine(productId, 2)];

        using var client = await factory.CreateOrdersClientAsync();

        using var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/orders/checkout")
        {
            Content = JsonContent.Create(new CheckoutOrder("Alice Example", "10 High Street, Glasgow G77", "card", "4242"))
        };
        checkoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", OrdersFactory.CreateCustomerToken(customerA));
        var checkoutResp = await client.SendAsync(checkoutReq);
        Assert.Equal(HttpStatusCode.Created, checkoutResp.StatusCode);

        var created = await checkoutResp.Content.ReadFromJsonAsync<OrderDetailsDto>();
        Assert.NotNull(created);
        Assert.Equal("pending_payment", created!.Status);
        Assert.Equal(298.00m, created.TotalAmount);
        Assert.Empty(factory.BasketClient.Baskets[customerA]);

        // Customer B cannot read Customer A's order (BOLA protection)
        using var bolaReq = new HttpRequestMessage(HttpMethod.Get, $"/orders/me/{created.Id}");
        bolaReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", OrdersFactory.CreateCustomerToken(customerB));
        var bolaResp = await client.SendAsync(bolaReq);
        Assert.Equal(HttpStatusCode.NotFound, bolaResp.StatusCode);

        // Simulate PaymentCaptured from Davids.Services.Payments -> transitions to completed + writes order_completed outbox
        using (var scope = factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOrdersService>();
            await svc.ApplyPaymentCapturedAsync(new PaymentCaptured(Guid.NewGuid(), created.Id, customerA, 298.00m, "GBP", "PAY-TEST1234"));

            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            Assert.Contains(await db.Outbox.ToListAsync(), m => m.Type == "order_placed");
            Assert.Contains(await db.Outbox.ToListAsync(), m => m.Type == "order_completed");
        }

        // Workforce auditor can inspect the completed order
        using var adminGet = new HttpRequestMessage(HttpMethod.Get, $"/orders/{created.Id}");
        adminGet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", OrdersFactory.CreateWorkforceToken(Guid.NewGuid(), "auditor"));
        var adminResp = await client.SendAsync(adminGet);
        Assert.Equal(HttpStatusCode.OK, adminResp.StatusCode);
        var completed = await adminResp.Content.ReadFromJsonAsync<OrderDetailsDto>();
        Assert.NotNull(completed);
        Assert.Equal("completed", completed!.Status);
        Assert.Equal("PAY-TEST1234", completed.PaymentReference);
    }
}
