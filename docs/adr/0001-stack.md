# ADR 0001: Technology stack

- **Status:** Accepted
- **Date:** 2026-10-22
- **Deciders:** HookGuard maintainers

## Context

HookGuard is a webhook gateway: it must receive callbacks from payment providers, verify them,
persist them durably, deliver them at least once, retry with backoff and expose the whole thing
through a live operations dashboard.

The project doubles as a portfolio piece aimed at backend roles in the Kenyan and South African
fintech market, where .NET plus a SQL database is the dominant backend pairing.

The stack has to satisfy three constraints at once:

1. **Correctness primitives are real, not simulated** — the project's value is in reliability, so
   the database, broker and cache must expose the primitives the design leans on: partial indexes,
   `jsonb`, row-level locking, `FOR UPDATE SKIP LOCKED`; message acks, prefetch, dead-letter
   exchanges; distributed counters with TTLs.
2. **The author must be able to defend every line.** Learning a new language or framework on this
   project would move the time budget from the hard parts (reliability, security) to the easy ones.
3. **One command to run it.** A reviewer should reach a working demo without a setup document.

## Decision

| Layer | Choice |
|---|---|
| Backend | C# / .NET 10, ASP.NET Core minimal APIs |
| ORM | EF Core with Npgsql, raw SQL where the ORM is the wrong tool |
| Database | PostgreSQL |
| Broker | RabbitMQ |
| Cache | Redis |
| Frontend | Angular 20+ (standalone components, signals) with TypeScript |
| Realtime | SignalR |
| Observability | OpenTelemetry, Serilog, Prometheus, Grafana |
| Testing | xUnit, FluentAssertions, Testcontainers, Playwright, k6 |
| DevOps | Docker Compose, GitHub Actions |

## Consequences

**Positive**

- PostgreSQL is the single source of truth; the delivery pipeline is derived from it, which makes
  the durability guarantee easy to reason about and easy to test.
- RabbitMQ gives genuine broker semantics (manual acks, prefetch, dead-lettering) that a database
  poller cannot demonstrate, while the outbox removes dual-write risk.
- Strong typing end to end: the Domain and Application layers are compiled against intent, and
  typed `HttpClient` usage in the delivery worker catches contract mistakes at build time.
- The Angular client is first-class for SignalR, which matters for a live dashboard.

**Negative / accepted costs**

- Two infrastructure systems (Postgres and RabbitMQ) plus Redis means more moving parts to run and
  more failure modes to test. Docker Compose contains the cost locally; Testcontainers contains it
  in CI.
- EF Core hides SQL. Mitigated by keeping raw SQL for the two hot queries (due deliveries, outbox
  relay) where the query shape actually matters.
- Angular is heavier than a small React app. Accepted: this dashboard is data-dense and stateful
  rather than an AI chat surface, and structured DI, typed forms and RxJS fit that shape.

## Alternatives considered

- **Node.js / NestJS backend.** Would have reduced context switching with the frontend, but the
  demonstrated reliability idioms (typed clients, `BackgroundService`, `CryptographicOperations`)
  are weaker lessons for the target market.
- **Kafka instead of RabbitMQ.** Better throughput and replayable log semantics, but the operational
  weight is out of proportion for a demo and the per-message retry/DLX story is less direct.
- **React frontend.** React's current edge is AI-facing UI; that is not this project. The API
  contract is kept OpenAPI-first so a React port stays cheap.
- **SQLite or SQL Server.** SQLite cannot demonstrate `SKIP LOCKED`, partial indexes and `jsonb`
  the way the design needs; SQL Server is not the market default for new work here.

## References

- ADR 0002 — Clean Architecture layering
- ADR 0006 — Postgres as source of truth, RabbitMQ as dispatcher
