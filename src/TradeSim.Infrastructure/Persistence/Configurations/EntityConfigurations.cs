using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeSim.Domain.Accounts;
using TradeSim.Domain.Events;
using TradeSim.Domain.Market;
using TradeSim.Domain.Orders;
using TradeSim.Domain.Portfolio;
using TradeSim.Domain.Users;
using TradeSim.Infrastructure.Outbox;

namespace TradeSim.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.Username).HasMaxLength(50);
        builder.HasIndex(u => u.Username).IsUnique();
    }
}

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Version).IsRowVersion();

        builder.HasOne<User>().WithOne().HasForeignKey<Account>(a => a.UserId);
        builder.ToTable(t => t.HasCheckConstraint("ck_accounts_reserved_cash", "reserved_cash >= 0"));

        builder.OwnsOne(a => a.RiskLimits, limits =>
        {
            limits.ToTable("risk_limits");
            limits.Property<Guid>("AccountId").HasColumnName("account_id");
            limits.WithOwner().HasForeignKey("AccountId");
            limits.HasKey("AccountId");
            limits.Property(l => l.MarginRate).HasPrecision(5, 4);
        });
        builder.Navigation(a => a.RiskLimits).IsRequired();
    }
}

internal sealed class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    public void Configure(EntityTypeBuilder<Instrument> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Symbol).HasMaxLength(10);
        builder.Property(i => i.Name).HasMaxLength(100);
        builder.HasIndex(i => i.Symbol).IsUnique();
    }
}

internal sealed class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Version).IsRowVersion();
        builder.Ignore(p => p.AvailableQuantity);

        builder.HasOne<Account>().WithMany().HasForeignKey(p => p.AccountId);
        builder.HasOne<Instrument>().WithMany().HasForeignKey(p => p.InstrumentId);
        builder.HasIndex(p => new { p.AccountId, p.InstrumentId }).IsUnique();
        builder.ToTable(t => t.HasCheckConstraint("ck_positions_quantities", "quantity >= reserved_quantity AND reserved_quantity >= 0"));
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Version).IsRowVersion();
        builder.Property(o => o.IdempotencyKey).HasMaxLength(100);
        builder.Property(o => o.RejectReason).HasMaxLength(200);

        builder.HasOne<Account>().WithMany().HasForeignKey(o => o.AccountId);
        builder.HasOne<Instrument>().WithMany().HasForeignKey(o => o.InstrumentId);

        builder.HasIndex(o => new { o.AccountId, o.IdempotencyKey }).IsUnique();

        builder.HasIndex(o => o.InstrumentId)
            .HasFilter("status = 'Open'")
            .HasDatabaseName("ix_orders_open_by_instrument");

        builder.HasIndex(o => new { o.AccountId, o.CreatedAt }).IsDescending(false, true);
    }
}

internal sealed class AccountEventConfiguration : IEntityTypeConfiguration<AccountEvent>
{
    public void Configure(EntityTypeBuilder<AccountEvent> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Type).HasMaxLength(50);
        builder.Property(e => e.Payload).HasColumnType("jsonb");
        builder.Property(e => e.CorrelationId).HasMaxLength(64);

        builder.HasOne<Account>().WithMany().HasForeignKey(e => e.AccountId);

        builder.HasIndex(e => new { e.AccountId, e.Version }).IsUnique();
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(100);
        builder.Property(m => m.Payload).HasColumnType("jsonb");
        builder.Property(m => m.CorrelationId).HasMaxLength(64);

        builder.HasIndex(m => m.CreatedAt)
            .HasFilter("published_at IS NULL")
            .HasDatabaseName("ix_outbox_messages_unpublished");
    }
}

internal sealed class PriceCandleConfiguration : IEntityTypeConfiguration<PriceCandle>
{
    public void Configure(EntityTypeBuilder<PriceCandle> builder)
    {
        builder.HasKey(c => new { c.InstrumentId, c.Minute });
        builder.HasOne<Instrument>().WithMany().HasForeignKey(c => c.InstrumentId);
    }
}

internal sealed class PortfolioSnapshotConfiguration : IEntityTypeConfiguration<PortfolioSnapshot>
{
    public void Configure(EntityTypeBuilder<PortfolioSnapshot> builder)
    {
        builder.HasKey(s => s.EventId);
        builder.Property(s => s.EventId).ValueGeneratedNever();
        builder.HasOne<Account>().WithMany().HasForeignKey(s => s.AccountId);
        builder.HasIndex(s => new { s.AccountId, s.TakenAt });
    }
}
