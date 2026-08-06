# HarborOps

Modular monolith on .NET 10. One ASP.NET Core API (`HarborOps.Api`), one separately hosted Blazor
frontend (`HarborOps.Web`), business capabilities split into module projects under `src/Modules/*`.

Source of truth for the full onboarding narrative: `docs/architecture.md` and `AGENTS.md`.
This file is the working rule set — follow it for every change.

## Modules and their SQL schemas

| Module | Schema | Module | Schema |
| --- | --- | --- | --- |
| Fleet | `fleet` | Maintenance | `maintenance` |
| Crew | `crew` | Compliance | `compliance` |
| Voyages | `voyages` | Documents | `documents` |
| Cargo | `cargo` | Identity | `identity` |

**Fleet is the reference implementation.** Match its naming, layout, type visibility, registration,
validation, and testing. The other modules have only a project and a module marker so far.

## Boundaries (non-negotiable)

- Business modules **never** reference each other. `HarborOps.Api` and `HarborOps.Infrastructure`
  reference modules, not the other way around.
- Cross-module links use **scalar identifiers** (`VesselId`, `CrewMemberId`). No EF Core navigation
  properties across module boundaries.
- No business logic in `HarborOps.Api`, controllers, or `HarborOps.Infrastructure`.
- One database, one `HarborOpsDbContext`, one global migration sequence. Never add a second
  application `DbContext` or a per-module migration sequence.
- Migrations live only in `src/HarborOps.Infrastructure/Persistence/Migrations` — never in a module
  project.
- Module implementation types stay `internal` unless they are a deliberate public contract or the
  composition marker.
- `HarborOps.SharedKernel` holds small stable primitives only — not a general helpers project.
- Do not introduce generic repositories, MediatR, event sourcing, message brokers, microservices, or
  extra abstraction layers without an explicitly approved architectural decision.
- Do not add package or project references ahead of need. Package versions are centrally pinned in
  `Directory.Packages.props`.
- Never commit secrets, passwords, or developer-specific connection strings.

## Module folder layout

Each module is a single project with these directories:

| Directory | Contents |
| --- | --- |
| `Domain` | Entities, value objects, invariants, business rules |
| `Application` | Use-case orchestration and application services |
| `Infrastructure` | EF Core mappings and module-specific adapters |
| `Contracts` | Purpose-built request/response and integration contracts |
| `Presentation` | Controllers or endpoint definitions |

Do not split a module into extra projects unless its size or a stricter boundary genuinely requires it.

Reference flow (`POST /api/fleet/vessels`):
`VesselsController` → `RegisterVesselService` (+ `RegisterVesselValidator`) → `Vessel.Register(...)`
→ `DbContext.Set<Vessel>()` → `VesselConfiguration` → `fleet.Vessels`.

## Implementing a feature

1. Pick the module that **owns the business rule** — not the API or Infrastructure project.
2. Model the behavior in `Domain`.
3. Add purpose-built request/response types in `Contracts`.
4. Wire validation, domain behavior, and persistence in `Application`.
5. Add an `internal IEntityTypeConfiguration<T>` in the module's `Infrastructure`, mapped explicitly
   to the module's schema.
6. Expose it from `Presentation`.
7. Register services in the module marker (e.g. `AddFleetModule()`); the API calls it at composition.
8. Add unit tests for business behavior and integration tests for the HTTP/persistence path.
9. Add a migration only when the data model actually changes.
10. Run the full check sequence before opening a PR.

Keep features vertical and capability-specific. Flow `CancellationToken` through async HTTP and
database operations. Name things descriptively after the capability (`AddCrewCertification`).

## Local setup

Requires .NET SDK `10.0.302` (pinned via `global.json`) and SQL Server access.

```powershell
dotnet tool restore
dotnet restore HarborOps.slnx
```

The API reads `ConnectionStrings:HarborOpsDatabase`. Override it per session instead of editing the
tracked `appsettings.json`:

```powershell
$env:ConnectionStrings__HarborOpsDatabase = "Server=localhost;Database=HarborOpsDevDb;Integrated Security=True;TrustServerCertificate=True"
```

Run: `dotnet run --project src/HarborOps.Api` (HTTP `:5204`, HTTPS `:7076`, health `/health`,
OpenAPI `/openapi/v1.json` in Development) and `dotnet run --project src/HarborOps.Web`
(HTTP `:5087`, HTTPS `:7276`). Web is hosted separately and has no API client configured yet.

## Check sequence — run all four before handing off

```powershell
dotnet restore HarborOps.slnx
dotnet build HarborOps.slnx --no-restore
dotnet test HarborOps.slnx --no-build
dotnet format HarborOps.slnx --verify-no-changes --no-restore
```

Warnings are errors, nullable reference types are on, latest analyzer level.

## EF Core migrations

Before creating one: sync with the target branch, confirm the build succeeds, confirm the entity and
mapping sit in the right module, confirm every new table is mapped to its module's schema, and run
`dotnet tool restore`.

```powershell
dotnet tool run dotnet-ef migrations add <MigrationName> `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext `
  --output-dir Persistence/Migrations
```

```powershell
dotnet tool run dotnet-ef database update `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext
```

Review all three generated files (migration, designer, `HarborOpsDbContextModelSnapshot.cs`): schema,
table and column names, nullability, indexes, foreign keys, destructive operations, and any
unexpected changes bleeding in from other modules.

- **Never modify or delete a migration already applied to a shared environment** — write a corrective
  migration instead.
- `migrations remove` is only for the last migration when it is unshared and applied nowhere outside a
  disposable local database.
- Do not hand-edit migration timestamps to force ordering. On a snapshot conflict, merge the target
  branch first, then regenerate your own unshared migration and re-run build and tests.
- A deployment script (`migrations script --idempotent`) is an artifact: review before running, and do
  not commit it unless the deployment process explicitly requires it.
- Keep unrelated module changes out of the same migration or PR when they can be separated.

## Tests

- `tests/HarborOps.UnitTests` — fast business/application behavior, no shared SQL Server.
- `tests/HarborOps.IntegrationTests` — HTTP behavior, controller wiring, serialization, DI,
  persistence. The API test factory swaps SQL Server for an isolated EF Core in-memory database.
- `tests/HarborOps.ArchitectureTests` — structural rules: modules don't reference each other, and
  Infrastructure discovers every module assembly.

## PR checklist

- [ ] Feature sits in the owning business module.
- [ ] No project reference added between business modules.
- [ ] Cross-module links use scalar identifiers.
- [ ] New tables use the owning module's SQL schema.
- [ ] Implementation types are internal unless deliberately public.
- [ ] No business rules in controllers or the composition root.
- [ ] Unit and integration tests cover the meaningful behavior.
- [ ] Database changes ship as one reviewed global migration.
- [ ] No secrets or developer-specific settings committed.
- [ ] Restore, build, tests, and format verification pass.
- [ ] Change is focused and introduces no unrequested architecture.
