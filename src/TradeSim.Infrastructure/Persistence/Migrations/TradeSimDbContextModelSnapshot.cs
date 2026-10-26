using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using TradeSim.Infrastructure.Persistence;

#nullable disable

namespace TradeSim.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(TradeSimDbContext))]
    partial class TradeSimDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "10.0.13")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("TradeSim.Domain.Accounts.Account", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<decimal>("Cash")
                        .HasPrecision(18, 4)
                        .HasColumnType("numeric(18,4)")
                        .HasColumnName("cash");

                    b.Property<decimal>("InitialCash")
                        .HasPrecision(18, 4)
                        .HasColumnType("numeric(18,4)")
                        .HasColumnName("initial_cash");

                    b.Property<decimal>("ReservedCash")
                        .HasPrecision(18, 4)
                        .HasColumnType("numeric(18,4)")
                        .HasColumnName("reserved_cash");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid")
                        .HasColumnName("user_id");

                    b.Property<uint>("Version")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("xid")
                        .HasColumnName("xmin");

                    b.HasKey("Id")
                        .HasName("pk_accounts");

                    b.HasIndex("UserId")
                        .IsUnique()
                        .HasDatabaseName("ix_accounts_user_id");

                    b.ToTable("accounts", null, t =>
                        {
                            t.HasCheckConstraint("ck_accounts_reserved_cash", "reserved_cash >= 0");
                        });
                });

            modelBuilder.Entity("TradeSim.Domain.Events.AccountEvent", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<Guid>("AccountId")
                        .HasColumnType("uuid")
                        .HasColumnName("account_id");

                    b.Property<string>("CorrelationId")
                        .HasMaxLength(64)
                        .HasColumnType("character varying(64)")
                        .HasColumnName("correlation_id");

                    b.Property<DateTimeOffset>("OccurredAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("occurred_at");

                    b.Property<string>("Payload")
                        .IsRequired()
                        .HasColumnType("jsonb")
                        .HasColumnName("payload");

                    b.Property<string>("Type")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("type");

                    b.Property<long>("Version")
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_account_events");

                    b.HasIndex("AccountId", "Version")
                        .IsUnique()
                        .HasDatabaseName("ix_account_events_account_id_version");

                    b.ToTable("account_events", (string)null);
                });

            modelBuilder.Entity("TradeSim.Domain.Market.Instrument", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<decimal>("LastPrice")
                        .HasPrecision(18, 4)
                        .HasColumnType("numeric(18,4)")
                        .HasColumnName("last_price");

                    b.Property<decimal>("MaxMovePct")
                        .HasPrecision(18, 4)
                        .HasColumnType("numeric(18,4)")
                        .HasColumnName("max_move_pct");

                    b.Property<int>("MinQuantity")
                        .HasColumnType("integer")
                        .HasColumnName("min_quantity");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("name");

                    b.Property<string>("Symbol")
                        .IsRequired()
                        .HasMaxLength(10)
                        .HasColumnType("character varying(10)")
                        .HasColumnName("symbol");

                    b.HasKey("Id")
                        .HasName("pk_instruments");

                    b.HasIndex("Symbol")
                        .IsUnique()
                        .HasDatabaseName("ix_instruments_symbol");

                    b.ToTable("instruments", (string)null);
                });

            modelBuilder.Entity("TradeSim.Domain.Market.PriceCandle", b =>
                {
                    b.Property<Guid>("InstrumentId")
                        .HasColumnType("uuid")
                        .HasColumnName("instrument_id");

                    b.Property<DateTimeOffset>("Minute")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("minute");

                    b.Property<decimal>("Close")
                        .HasPrecision(18, 4)
                        .HasColumnType("numeric(18,4)")
                        .HasColumnName("close");

                    b.Property<decimal>("High")
                        .HasPrecision(18, 4)
                        .HasColumnType("numeric(18,4)")
                        .HasColumnName("high");

                    b.Property<decimal>("Low")
                        .HasPrecision(18, 4)
                        .HasColumnType("numeric(18,4)")
                        .HasColumnName("low");

                    b.Property<decimal>("Open")
                        .HasPrecision(18, 4)
                        .HasColumnType("numeric(18,4)")
                        .HasColumnName("open");

                    b.HasKey("InstrumentId", "Minute")
                        .HasName("pk_price_candles");

                    b.ToTable("price_candles", (string)null);
                });

            modelBuilder.Entity("TradeSim.Domain.Orders.Order", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<Guid>("AccountId")
                        .HasColumnType("uuid")
                        .HasColumnName("account_id");

                    b.Property<decimal?>("Commission")
                        .HasPrecision(18, 4)
                        .HasColumnType("numeric(18,4)")
                        .HasColumnName("commission");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<DateTimeOffset?>("ExecutedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("executed_at");

                    b.Property<decimal?>("ExecutedPrice")
                        .HasPrecision(18, 4)
                        .HasColumnType("numeric(18,4)")
                        .HasColumnName("executed_price");

                    b.Property<string>("IdempotencyKey")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("idempotency_key");

                    b.Property<Guid>("InstrumentId")
                        .HasColumnType("uuid")
                        .HasColumnName("instrument_id");

                    b.Property<decimal?>("LimitPrice")
                        .HasPrecision(18, 4)
                        .HasColumnType("numeric(18,4)")
                        .HasColumnName("limit_price");

                    b.Property<int>("Quantity")
                        .HasColumnType("integer")
                        .HasColumnName("quantity");

                    b.Property<string>("RejectReason")
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("reject_reason");

                    b.Property<decimal>("ReservedAmount")
                        .HasPrecision(18, 4)
                        .HasColumnType("numeric(18,4)")
                        .HasColumnName("reserved_amount");

                    b.Property<string>("Side")
                        .IsRequired()
                        .HasMaxLength(10)
                        .HasColumnType("character varying(10)")
                        .HasColumnName("side");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("character varying(20)")
                        .HasColumnName("status");

                    b.Property<string>("TimeInForce")
                        .IsRequired()
                        .HasMaxLength(10)
                        .HasColumnType("character varying(10)")
                        .HasColumnName("time_in_force");

                    b.Property<decimal?>("TriggerPrice")
                        .HasPrecision(18, 4)
                        .HasColumnType("numeric(18,4)")
                        .HasColumnName("trigger_price");

                    b.Property<string>("Type")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("character varying(20)")
                        .HasColumnName("type");

                    b.Property<DateTimeOffset>("UpdatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("updated_at");

                    b.Property<uint>("Version")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("xid")
                        .HasColumnName("xmin");

                    b.HasKey("Id")
                        .HasName("pk_orders");

                    b.HasIndex("InstrumentId")
                        .HasDatabaseName("ix_orders_open_by_instrument")
                        .HasFilter("status = 'Open'");

                    b.HasIndex("AccountId", "CreatedAt")
                        .IsDescending(false, true)
                        .HasDatabaseName("ix_orders_account_id_created_at");

                    b.HasIndex("AccountId", "IdempotencyKey")
                        .IsUnique()
                        .HasDatabaseName("ix_orders_account_id_idempotency_key");

                    b.ToTable("orders", (string)null);
                });

            modelBuilder.Entity("TradeSim.Domain.Portfolio.PortfolioSnapshot", b =>
                {
                    b.Property<Guid>("EventId")
                        .HasColumnType("uuid")
                        .HasColumnName("event_id");

                    b.Property<Guid>("AccountId")
                        .HasColumnType("uuid")
                        .HasColumnName("account_id");

                    b.Property<decimal>("Equity")
                        .HasPrecision(18, 4)
                        .HasColumnType("numeric(18,4)")
                        .HasColumnName("equity");

                    b.Property<DateTimeOffset>("TakenAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("taken_at");

                    b.HasKey("EventId")
                        .HasName("pk_portfolio_snapshots");

                    b.HasIndex("AccountId", "TakenAt")
                        .HasDatabaseName("ix_portfolio_snapshots_account_id_taken_at");

                    b.ToTable("portfolio_snapshots", (string)null);
                });

            modelBuilder.Entity("TradeSim.Domain.Portfolio.Position", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<Guid>("AccountId")
                        .HasColumnType("uuid")
                        .HasColumnName("account_id");

                    b.Property<decimal>("AvgCost")
                        .HasPrecision(18, 4)
                        .HasColumnType("numeric(18,4)")
                        .HasColumnName("avg_cost");

                    b.Property<Guid>("InstrumentId")
                        .HasColumnType("uuid")
                        .HasColumnName("instrument_id");

                    b.Property<int>("Quantity")
                        .HasColumnType("integer")
                        .HasColumnName("quantity");

                    b.Property<decimal>("RealizedPnl")
                        .HasPrecision(18, 4)
                        .HasColumnType("numeric(18,4)")
                        .HasColumnName("realized_pnl");

                    b.Property<int>("ReservedQuantity")
                        .HasColumnType("integer")
                        .HasColumnName("reserved_quantity");

                    b.Property<uint>("Version")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("xid")
                        .HasColumnName("xmin");

                    b.HasKey("Id")
                        .HasName("pk_positions");

                    b.HasIndex("InstrumentId")
                        .HasDatabaseName("ix_positions_instrument_id");

                    b.HasIndex("AccountId", "InstrumentId")
                        .IsUnique()
                        .HasDatabaseName("ix_positions_account_id_instrument_id");

                    b.ToTable("positions", null, t =>
                        {
                            t.HasCheckConstraint("ck_positions_quantities", "quantity >= reserved_quantity AND reserved_quantity >= 0");
                        });
                });

            modelBuilder.Entity("TradeSim.Domain.Users.User", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<string>("PasswordHash")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("password_hash");

                    b.Property<string>("Username")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("username");

                    b.HasKey("Id")
                        .HasName("pk_users");

                    b.HasIndex("Username")
                        .IsUnique()
                        .HasDatabaseName("ix_users_username");

                    b.ToTable("users", (string)null);
                });

            modelBuilder.Entity("TradeSim.Infrastructure.Outbox.OutboxMessage", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<string>("CorrelationId")
                        .HasMaxLength(64)
                        .HasColumnType("character varying(64)")
                        .HasColumnName("correlation_id");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<string>("Payload")
                        .IsRequired()
                        .HasColumnType("jsonb")
                        .HasColumnName("payload");

                    b.Property<DateTimeOffset?>("PublishedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("published_at");

                    b.Property<string>("Type")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("type");

                    b.HasKey("Id")
                        .HasName("pk_outbox_messages");

                    b.HasIndex("CreatedAt")
                        .HasDatabaseName("ix_outbox_messages_unpublished")
                        .HasFilter("published_at IS NULL");

                    b.ToTable("outbox_messages", (string)null);
                });

            modelBuilder.Entity("TradeSim.Domain.Accounts.Account", b =>
                {
                    b.HasOne("TradeSim.Domain.Users.User", null)
                        .WithOne()
                        .HasForeignKey("TradeSim.Domain.Accounts.Account", "UserId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_accounts_users_user_id");

                    b.OwnsOne("TradeSim.Domain.Accounts.RiskLimits", "RiskLimits", b1 =>
                        {
                            b1.Property<Guid>("AccountId")
                                .HasColumnType("uuid")
                                .HasColumnName("account_id");

                            b1.Property<decimal>("MarginRate")
                                .HasPrecision(5, 4)
                                .HasColumnType("numeric(5,4)")
                                .HasColumnName("margin_rate");

                            b1.Property<int>("MaxOpenOrders")
                                .HasColumnType("integer")
                                .HasColumnName("max_open_orders");

                            b1.Property<decimal>("MaxOrderValue")
                                .HasPrecision(18, 4)
                                .HasColumnType("numeric(18,4)")
                                .HasColumnName("max_order_value");

                            b1.Property<int>("MaxPositionQuantity")
                                .HasColumnType("integer")
                                .HasColumnName("max_position_quantity");

                            b1.HasKey("AccountId")
                                .HasName("pk_risk_limits");

                            b1.ToTable("risk_limits", (string)null);

                            b1.WithOwner()
                                .HasForeignKey("AccountId")
                                .HasConstraintName("fk_risk_limits_accounts_id");
                        });

                    b.Navigation("RiskLimits")
                        .IsRequired();
                });

            modelBuilder.Entity("TradeSim.Domain.Events.AccountEvent", b =>
                {
                    b.HasOne("TradeSim.Domain.Accounts.Account", null)
                        .WithMany()
                        .HasForeignKey("AccountId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_account_events_accounts_account_id");
                });

            modelBuilder.Entity("TradeSim.Domain.Market.PriceCandle", b =>
                {
                    b.HasOne("TradeSim.Domain.Market.Instrument", null)
                        .WithMany()
                        .HasForeignKey("InstrumentId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_price_candles_instruments_instrument_id");
                });

            modelBuilder.Entity("TradeSim.Domain.Orders.Order", b =>
                {
                    b.HasOne("TradeSim.Domain.Accounts.Account", null)
                        .WithMany()
                        .HasForeignKey("AccountId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_orders_accounts_account_id");

                    b.HasOne("TradeSim.Domain.Market.Instrument", null)
                        .WithMany()
                        .HasForeignKey("InstrumentId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_orders_instruments_instrument_id");
                });

            modelBuilder.Entity("TradeSim.Domain.Portfolio.PortfolioSnapshot", b =>
                {
                    b.HasOne("TradeSim.Domain.Accounts.Account", null)
                        .WithMany()
                        .HasForeignKey("AccountId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_portfolio_snapshots_accounts_account_id");
                });

            modelBuilder.Entity("TradeSim.Domain.Portfolio.Position", b =>
                {
                    b.HasOne("TradeSim.Domain.Accounts.Account", null)
                        .WithMany()
                        .HasForeignKey("AccountId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_positions_accounts_account_id");

                    b.HasOne("TradeSim.Domain.Market.Instrument", null)
                        .WithMany()
                        .HasForeignKey("InstrumentId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired()
                        .HasConstraintName("fk_positions_instruments_instrument_id");
                });
#pragma warning restore 612, 618
        }
    }
}
