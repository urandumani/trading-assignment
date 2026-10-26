using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradeSim.Infrastructure.Persistence.Migrations
{
    public partial class InitialSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instruments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    symbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    max_move_pct = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    min_quantity = table.Column<int>(type: "integer", nullable: false),
                    last_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instruments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "price_candles",
                columns: table => new
                {
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    minute = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    open = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    high = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    low = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    close = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_candles", x => new { x.instrument_id, x.minute });
                    table.ForeignKey(
                        name: "fk_price_candles_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    initial_cash = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cash = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    reserved_cash = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.CheckConstraint("ck_accounts_reserved_cash", "reserved_cash >= 0");
                    table.ForeignKey(
                        name: "fk_accounts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_events_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    side = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    time_in_force = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    limit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    trigger_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reserved_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    executed_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    executed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    commission = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    reject_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_orders_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orders_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "portfolio_snapshots",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    equity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portfolio_snapshots", x => x.event_id);
                    table.ForeignKey(
                        name: "fk_portfolio_snapshots_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    reserved_quantity = table.Column<int>(type: "integer", nullable: false),
                    avg_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    realized_pnl = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positions", x => x.id);
                    table.CheckConstraint("ck_positions_quantities", "quantity >= reserved_quantity AND reserved_quantity >= 0");
                    table.ForeignKey(
                        name: "fk_positions_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_positions_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "risk_limits",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    margin_rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    max_position_quantity = table.Column<int>(type: "integer", nullable: false),
                    max_order_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    max_open_orders = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_risk_limits", x => x.account_id);
                    table.ForeignKey(
                        name: "fk_risk_limits_accounts_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_events_account_id_version",
                table: "account_events",
                columns: new[] { "account_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_user_id",
                table: "accounts",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_instruments_symbol",
                table: "instruments",
                column: "symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_account_id_created_at",
                table: "orders",
                columns: new[] { "account_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_orders_account_id_idempotency_key",
                table: "orders",
                columns: new[] { "account_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_open_by_instrument",
                table: "orders",
                column: "instrument_id",
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_unpublished",
                table: "outbox_messages",
                column: "created_at",
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_snapshots_account_id_taken_at",
                table: "portfolio_snapshots",
                columns: new[] { "account_id", "taken_at" });

            migrationBuilder.CreateIndex(
                name: "ix_positions_account_id_instrument_id",
                table: "positions",
                columns: new[] { "account_id", "instrument_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_positions_instrument_id",
                table: "positions",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_username",
                table: "users",
                column: "username",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_events");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "portfolio_snapshots");

            migrationBuilder.DropTable(
                name: "positions");

            migrationBuilder.DropTable(
                name: "price_candles");

            migrationBuilder.DropTable(
                name: "risk_limits");

            migrationBuilder.DropTable(
                name: "instruments");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
