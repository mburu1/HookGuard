# Architecture Decision Records

Short, dated records of decisions that are expensive to reverse. Format follows
[Michael Nygard](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions):
**Context → Decision → Consequences → Alternatives considered**.

Add a new record as `NNNN-kebab-case-title.md` and link it in the table below and in the README.

| ADR | Title | Status |
|---|---|---|
| [0001](0001-stack.md) | Technology stack | Accepted |
| [0002](0002-clean-architecture.md) | Clean Architecture layering | Accepted |

Planned, referenced by the README:

| ADR | Title |
|---|---|
| 0003 | Persist-then-ack |
| 0004 | At-least-once delivery |
| 0005 | Transactional outbox |
| 0006 | Postgres as source of truth, RabbitMQ as dispatcher |
| 0007 | Full-jitter backoff |
| 0008 | Raw-body signature verification |
| 0009 | SignalR for live updates |
| 0010 | Angular signals + RxJS split |
