# ADR 0002: Clean Architecture layering

- **Status:** Accepted
- **Date:** 2026-10-22
- **Deciders:** HookGuard maintainers

## Context

The interesting logic in HookGuard is not infrastructure-shaped. Three examples:

- **Signature verification and replay tolerance.** "Is this payload authentic, and was it signed
  recently enough to be trusted?" is a pure function of raw bytes, a secret, a timestamp, a
  tolerance and a clock.
- **Retry scheduling.** "Given attempt `n`, what is the next attempt time?" is a pure function of
  attempt count, a base delay, a cap and a random source.
- **Delivery status transitions.** `Pending → InFlight → Succeeded | Failed → DeadLettered` is a
  state machine with legal and illegal moves.

All three are the parts most worth testing exhaustively, and all three are the parts most easily
poisoned by framework coupling — a verifier that takes an `HttpRequest` can only be tested through
a web host, and a backoff calculation that reads `DateTime.UtcNow` cannot be tested for
determinism at all.

The project also has two different hosts (an HTTP API and a background worker) that must share the
same use cases without sharing a web stack.

## Decision

Four projects with dependencies pointing strictly inward.

```
HookGuard.Domain          entities, value objects, domain rules      → no dependencies
HookGuard.Application     use cases, interfaces, abstractions        → Domain
HookGuard.Infrastructure  EF Core, RabbitMQ, Redis, HTTP, crypto     → Application
HookGuard.Api             ingest + management API, SignalR, auth     → Infrastructure, Application
HookGuard.Workers         outbox relay, delivery workers, scheduler  → Infrastructure, Application
```

Rules:

1. `Domain` and `Application` never reference EF Core, RabbitMQ, Redis, Npgsql, ASP.NET Core or any
   other third-party infrastructure package. They may reference only the BCL.
2. Infrastructure implements interfaces declared in `Application` (`IEventRepository`, `IClock`,
   `IDispatcher`, `ISignatureVerifier`, `IRandom`). Nothing in `Application` knows which
   implementation is registered.
3. `Api` and `Workers` are composition roots: they own DI registration, configuration binding and
   hosting. They hold no business logic.
4. The rule is not a convention. `HookGuard.ArchitectureTests` uses `NetArchTest` to assert that no
   `Domain` type depends on an outer namespace or on a forbidden package, so a violation fails the
   build.

## Consequences

**Positive**

- Time and randomness are injectable, so signature tolerance, backoff bounds and state transitions
  are unit-testable to the millisecond without a container, a web host or a sleep.
- The same `EventIngestService` runs behind the HTTP ingest endpoint and behind any future caller
  (CLI, message consumer, test harness).
- Infrastructure is replaceable in one project. Swapping RabbitMQ for another broker touches
  `Infrastructure` and the DI registration only.
- Test speed: the fast, high-value tests need no Docker.

**Negative / accepted costs**

- Five projects and more ceremony than a single-project API: mapping between domain entities and
  persistence models, more `.csproj` files and more DI wiring.
- Some indirection is admittedly future-facing — `IDispatcher` exists before a second implementation
  does. Accepted because the interface is the seam that makes the outbox pattern testable.
- Developers can leak infrastructure concerns through "just one" package reference. This is exactly
  what the architecture test prevents.

## Alternatives considered

- **Single project with folders.** Fastest to start, no boundary enforcement, and the signature and
  backoff logic would drift into depending on `HttpContext` and `DateTime.UtcNow`.
- **Vertical slice organisation.** Good for feature throughput; less clear about the "pure core
  versus infrastructure" split, which is the specific thing this project needs to demonstrate.
- **Layering enforced by convention and code review only.** Cheaper, but the whole point is that a
  reviewer can see the constraint automatically verified in CI.

## References

- ADR 0001 — Technology stack
- ADR 0003 — Persist-then-ack
- ADR 0005 — Transactional outbox
