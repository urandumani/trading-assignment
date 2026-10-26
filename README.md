# Market Trading Assignment

Trading simulation backend for the assignment built with .NET 10 and PostgreSQL. It has 12 fictional stocks, market and limit orders, stop-loss and take-profit orders, portfolio tracking, and a WebSocket feed.

## Running it

With Docker Desktop running, start the project from this folder:

```sh
docker compose up -d --build
```

The worker applies migrations and seeds the database on startup. Once the containers are healthy, open [Swagger](http://localhost:8080/swagger). Log in through `POST /api/v1/auth/login`, then paste the returned `accessToken` into **Authorize**.

The demo users are `trader1` through `trader5`, all with password `Password123!` and $100,000 starting balance. The first three have cash accounts; the last two have margin accounts.

Trading hours are 09:30-16:00 CEST, Monday to Friday. To try placing orders outside those hours, create a `.env` file with:

```dotenv
MARKET_ALWAYS_OPEN=true
```

Then run `docker compose up -d` again. Other settings are listed in `.env.example`; the defaults work without it.

Import the collection and environment from `postman/` to try the API in Postman. Select the **TradeSim (local Docker)** environment and run the folders in order. Login is handled by the collection.

For the live feed, connect to `ws://localhost:8080/ws?access_token=<accessToken>` and send:

```json
{"action":"subscribe","symbols":["APXL","NOVA"]}
```

[Aspire Dashboard](http://localhost:18888) has the logs, traces, and metrics. [RabbitMQ management](http://localhost:15672) uses `test` / `test`.

Use `docker compose down` to stop the stack. Adding `-v` also deletes the saved data, giving you a fresh database on the next start.

## My decisions & choices explained

I changed the route for listing a users orders to `GET /api/v1/users/{userId}/orders`. The PDF lists both `/orders/{userId}` and `/orders/{orderId}`, but both IDs are UUIDs, so there's a conflict in this case. Getting a single order still uses `GET /api/v1/orders/{orderId}`.

The example in the PDF uses `AAPL` (Appl inc), while the requirements ask for fictional assets. Use `APXL` instead or choose a symbol from the instruments endpoint.


The bonus mentions caching user sessions in memory. I have decided to go with JWTs with the user and account IDs in the token so there is no server-side session to cache. The instrument list is cached in memory and Redis holds live prices.

Some other decisions:

- Prices use a percentage random walk with occasional larger jumps, so the circuit breaker gets exercised too.
- Account changes use a database transaction and an account row lock, alongside optimistic concurrency checks.
- The audit history records account events and can rebuild cash balances and share quantities. Current balances are also stored in tables for queries, so this is a hybrid approach to the event-sourcing bonus. Otherwise I'd have to go with full event sourcing and use something like an event store database.
- One worker generates prices and processes resting orders. The API runs as two replicas by default. Scaling the worker would need coordination to avoid running those jobs twice.

Fills are all-or-nothing. There is no short selling, so stop-loss and take-profit orders are SELL-only. Margin accounts have buying-power checks, but no margin calls or forced liquidation. Rate limits are counted separately by each API replica.

## Testing

With the .NET 10 SDK and Docker installed:

```sh
dotnet test TradeSim.slnx
```

Integration tests start their own PostgreSQL, Redis, and RabbitMQ containers. They cover concurrent orders, transaction retries, order expiry, audit reconciliation, and dependency outages as well as the API responses.

The k6 script is in `load/k6/`, with saved results in `load/results/`. The recorded run reached 1,000 virtual users with no failed HTTP requests; p95 was about 73 ms for reads and 163 ms for orders.
