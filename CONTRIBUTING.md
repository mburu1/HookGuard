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

### Database migrations

The API creates its own schema on first boot in development, so `docker compose up` needs nothing
extra. To run the API outside Docker, start Postgres and apply migrations explicitly:

```bash
docker compose up -d postgres
dotnet tool restore
dotnet ef database update --project src/HookGuard.Infrastructure --startup-project src/HookGuard.Infrastructure
```

Adding a migration goes through the same local tool:

```bash
dotnet ef migrations add <Name> --project src/HookGuard.Infrastructure --startup-project src/HookGuard.Infrastructure --output-dir Persistence/Migrations
```

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
