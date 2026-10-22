# Contributing to HookGuard

Thanks for taking a look. This is primarily a portfolio-grade reliability project, so the bar is
"would I defend this in a code review", not "does it compile".

## Getting set up

```bash
git clone https://github.com/mburu1/HookGuard.git
cd HookGuard
cp .env.example .env
docker compose up --build     # Postgres, Redis, RabbitMQ, API, workers, fake merchant
dotnet test                   # unit, integration and architecture tests
```

Requires the .NET 10 SDK (pinned in `global.json`) and Docker for the integration tests.

## Branching

| Branch | Purpose |
|---|---|
| `main` | Always releasable, protected |
| `feat/…` | New capability |
| `fix/…` | Bug fix |
| `docs/…` | Documentation only |
| `chore/…` | Tooling, dependencies, build |

## Commits

[Conventional Commits](https://www.conventionalcommits.org/):

```
feat(ingest): verify HMAC against the raw request body
fix(delivery): reset in-flight deliveries after the visibility timeout
docs(adr): record the outbox decision
```

## Pull requests

- One milestone per PR (for example `M1.3`), linked to its issue.
- Fill in the PR template: summary, test evidence, risks, checklist.
- CI must be green: restore, build, tests, `dotnet format --verify-no-changes`.

## Quality gates

- `dotnet format HookGuard.slnx` before pushing.
- Warnings are errors; keep the build clean rather than suppressing with `#pragma`.
- New behaviour needs a test. Domain and Application are held at 80%+ coverage.
- Do not commit secrets. `.env` is ignored, `.env.example` documents the shape.
- Domain and Application must not reference EF Core, RabbitMQ, Redis or ASP.NET; the architecture
  test enforces this.
