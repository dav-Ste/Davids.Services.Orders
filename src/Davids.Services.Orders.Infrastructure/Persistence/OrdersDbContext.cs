using Davids.Services.Orders.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Davids.Services.Orders.Infrastructure.Persistence;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CustomerEmail).HasMaxLength(320);
            entity.Property(x => x.ShippingFullName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.ShippingAddress).HasMaxLength(300).IsRequired();
            entity.Property(x => x.PaymentMethod).HasMaxLength(24).IsRequired();
            entity.Property(x => x.CardLast4).HasMaxLength(4).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.TotalAmount).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.PaymentReference).HasMaxLength(64);
            entity.Property(x => x.FailureReason).HasMaxLength(400);
            entity.HasIndex(x => new { x.CustomerId, x.CreatedAt });
            entity.HasIndex(x => new { x.Status, x.CreatedAt });
            entity.HasMany(x => x.Items)
                .WithOne()
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("OrderItems");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Sku).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ProductName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.LineTotal).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.HasIndex(x => x.OrderId);
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("Outbox");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(64);
            entity.Property(x => x.Type).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Payload).IsRequired();
            entity.HasIndex(x => x.PublishedAt);
        });

        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("Inbox");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(128);
        });
    }
}

public sealed class OutboxMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
}

public sealed class InboxMessage
{
    public string Id { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
}
