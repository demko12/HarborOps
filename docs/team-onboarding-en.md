# HarborOps Team Onboarding Guide

**Audience:** Developers starting work on HarborOps  
**Purpose:** Explain how the solution is organized, how to develop safely inside its boundaries, and how to run and change the system locally.

---

## 1. HarborOps in one minute

HarborOps is a **.NET 10 modular monolith** for maritime operations management.

The backend is deployed as one ASP.NET Core API, but business capabilities are separated into modules. Each module owns its business rules and data model. The Blazor frontend is a separately hosted application and communicates with the API over HTTP.

The key architectural idea is:

> Keep deployment simple, while keeping business capabilities strongly separated in code and data.

Current business modules:

| Module | Responsibility | SQL schema |
| --- | --- | --- |
| Fleet | Vessels and fleet records | `fleet` |
| Crew | Crew members and assignments | `crew` |
| Voyages | Voyage planning and execution | `voyages` |
| Cargo | Cargo operations | `cargo` |
| Maintenance | Maintenance work | `maintenance` |
| Compliance | Regulatory and compliance work | `compliance` |
| Documents | Document management | `documents` |
| Identity | Users, roles, and access | `identity` |

At the moment, **Fleet is the reference implementation**. The other modules provide their project and module marker, ready for capability development.

---

## 2. Architecture

### 2.1 High-level structure

```text
HarborOps.Web
    |
    | HTTP
    v
HarborOps.Api
    +-- composes business modules
    +-- references HarborOps.Infrastructure
    |
    v
HarborOps.Infrastructure
    +-- SQL Server integration
    +-- HarborOpsDbContext
    +-- global EF Core migration sequence
    |
    v
Business modules
    +-- Domain
    +-- Application
    +-- Infrastructure
    +-- Contracts
    +-- Presentation
```

`HarborOps.Api` and `HarborOps.Infrastructure` reference the business modules to compose the application and discover persistence configurations. **Business modules never reference one another.**

### 2.2 Solution projects

| Project | Role |
| --- | --- |
| `src/HarborOps.Api` | Backend entry point and composition root |
| `src/HarborOps.Web` | Separately hosted Blazor Web frontend |
| `src/HarborOps.Infrastructure` | SQL Server, the shared `HarborOpsDbContext`, and migrations |
| `src/HarborOps.SharedKernel` | Small, stable primitives genuinely shared by modules |
| `src/Modules/*` | One project for each business capability |
| `tests/HarborOps.UnitTests` | Fast tests for business behavior |
| `tests/HarborOps.IntegrationTests` | API and persistence integration tests |
| `tests/HarborOps.ArchitectureTests` | Automated module-boundary checks |

### 2.3 Inside a module

Each module is one project with these folders:

| Folder | Put this here |
| --- | --- |
| `Domain` | Entities, value objects, invariants, and business rules |
| `Application` | Use-case orchestration and application services |
| `Infrastructure` | EF Core mappings and module-specific adapters |
| `Contracts` | Intentional request, response, and integration contracts |
| `Presentation` | API controllers or endpoint composition |

These are organizational boundaries inside the module project. Do not create extra projects unless module size or enforcement requirements justify them.

The Fleet vessel-registration flow is the example to follow:

```text
POST /api/fleet/vessels
        |
        v
VesselsController                  Presentation
        |
        v
RegisterVesselService             Application
        |
        +--> RegisterVesselValidator
        |
        v
Vessel.Register(...)              Domain
        |
        v
DbContext.Set<Vessel>()
        |
        v
VesselConfiguration              Infrastructure
        |
        v
fleet.Vessels                    SQL Server
```

### 2.4 Persistence model

HarborOps has:

- one SQL Server database;
- one `HarborOpsDbContext`;
- one globally ordered EF Core migration history;
- one SQL schema owned by each business module.

Module entity configurations implement `IEntityTypeConfiguration<T>`. `HarborOpsDbContext` discovers them through each module's public marker type.

Cross-module relationships use **scalar identifiers**, for example `VesselId` or `CrewMemberId`. They do not use EF Core navigation properties between modules.

---

## 3. Getting started locally

### 3.1 Prerequisites

Install or obtain:

- Git;
- .NET SDK `10.0.302` or a compatible .NET 10 patch selected by `global.json`;
- access to SQL Server;
- access to the repository;
- an IDE such as Visual Studio, Rider, or Visual Studio Code.

Check the SDK:

```powershell
dotnet --version
```

From the repository root, restore the local EF Core tool and dependencies:

```powershell
dotnet tool restore
dotnet restore HarborOps.slnx
```

### 3.2 Configure the database connection

The API reads:

```text
ConnectionStrings:HarborOpsDatabase
```

The repository currently defaults to the shared development SQL Server configured in `src/HarborOps.Api/appsettings.json`. Confirm that you have network and database access before running the API or applying migrations.

To override the connection string for the current PowerShell session without changing a tracked file:

```powershell
$env:ConnectionStrings__HarborOpsDatabase = "Server=localhost;Database=HarborOpsDevDb;Integrated Security=True;TrustServerCertificate=True"
```

Do not commit passwords or developer-specific connection strings.

### 3.3 Create or update the local database

Apply every migration that has not yet been applied:

```powershell
dotnet tool run dotnet-ef database update `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext
```

### 3.4 Run the applications

Run the API:

```powershell
dotnet run --project src/HarborOps.Api
```

Default development addresses:

- API HTTP: `http://localhost:5204`
- API HTTPS: `https://localhost:7076`
- Health check: `http://localhost:5204/health`
- OpenAPI document in Development: `http://localhost:5204/openapi/v1.json`

Run the Web application in a second terminal:

```powershell
dotnet run --project src/HarborOps.Web
```

Default development addresses:

- Web HTTP: `http://localhost:5087`
- Web HTTPS: `https://localhost:7276`

The Web project is separately hosted. It currently contains the frontend foundation and does not yet configure an API client.

### 3.5 Validate the repository

Run the full validation sequence from the repository root:

```powershell
dotnet restore HarborOps.slnx
dotnet build HarborOps.slnx --no-restore
dotnet test HarborOps.slnx --no-build
dotnet format HarborOps.slnx --verify-no-changes --no-restore
```

Warnings are treated as errors, nullable reference types are enabled, and the latest analyzer level is used.

---

## 4. How to implement a feature

Use this sequence for a normal backend capability:

1. **Choose the owning module.** Put the feature in the module that owns the business rule, not in the API or Infrastructure project.
2. **Model the domain behavior.** Add or change entities and rules under `Domain`.
3. **Define intentional contracts.** Put API request and response types under `Contracts`.
4. **Implement the use case.** Coordinate validation, domain behavior, and persistence under `Application`.
5. **Map persistence.** Add an internal `IEntityTypeConfiguration<T>` under the module's `Infrastructure` folder and map tables to that module's schema.
6. **Expose the capability.** Add the controller or endpoint under `Presentation`.
7. **Register services.** Register module services in the module marker, such as `AddFleetModule()`. The API calls that method at the composition root.
8. **Add tests.** Cover business behavior with unit tests and the HTTP/persistence path with integration tests.
9. **Add a migration when the database model changes.**
10. **Run the full validation sequence before opening a pull request.**

Keep the feature vertical and capability-specific. The Fleet module is the current reference for naming, placement, visibility, registration, validation, and testing.

---

## 5. What to do

- Keep every business capability inside its owning module.
- Keep module implementation types `internal` unless they are an intentional public contract or composition marker.
- Use scalar IDs for references to concepts owned by another module.
- Map every table explicitly to the owning module's SQL schema.
- Use the shared `DbContext` abstraction inside modules when persistence is needed.
- Keep domain rules close to the domain model.
- Pass `CancellationToken` through asynchronous HTTP and database operations.
- Add focused unit, integration, and architecture tests where appropriate.
- Add package and project references only when current code requires them.
- Manage package versions centrally in `Directory.Packages.props`.
- Keep migrations small, reviewed, and synchronized with the EF model snapshot.
- Use descriptive, capability-specific names such as `AddCrewCertification`.

---

## 6. What not to do

- Do not add a project reference from one business module to another.
- Do not create cross-module EF Core navigation properties.
- Do not place business logic in `HarborOps.Api`, controllers, or `HarborOps.Infrastructure`.
- Do not create a second application `DbContext` or a separate migration history for a module.
- Do not place migrations in module projects.
- Do not turn `HarborOps.SharedKernel` into a general utilities project.
- Do not make implementation classes public only to make them easier to access.
- Do not introduce generic repositories, MediatR, event sourcing, message brokers, microservices, or extra abstraction layers unless the architecture decision is explicitly approved.
- Do not add package references speculatively.
- Do not commit secrets, passwords, or personal connection strings.
- Do not edit or delete a migration that has already been applied to a shared environment; create a corrective migration.
- Do not mix unrelated module changes into the same migration or pull request when they can be separated.

---

## 7. EF Core migrations

### 7.1 Migration ownership

All migrations belong here:

```text
src/HarborOps.Infrastructure/Persistence/Migrations
```

Although each module owns its tables and SQL schema, HarborOps maintains **one global migration sequence**. A migration can technically contain changes from multiple modules, but a normal feature migration should remain focused on one coherent change.

### 7.2 Before creating a migration

1. Synchronize your branch with the latest target branch.
2. Confirm the application builds.
3. Confirm the entity and mapping are in the correct module.
4. Confirm every new table is mapped to the module's schema.
5. Restore the local tool with `dotnet tool restore`.

This reduces snapshot conflicts and accidental unrelated changes.

### 7.3 Create a migration

Run from the repository root:

```powershell
dotnet tool run dotnet-ef migrations add <MigrationName> `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext `
  --output-dir Persistence/Migrations
```

Example:

```powershell
dotnet tool run dotnet-ef migrations add AddCrewCertification `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext `
  --output-dir Persistence/Migrations
```

EF Core will update:

- the new migration file;
- the migration designer file;
- `HarborOpsDbContextModelSnapshot.cs`.

Review all three. Check schema names, table and column names, nullability, indexes, foreign keys, destructive operations, and unexpected changes from other modules.

### 7.4 Apply migrations

Apply all pending migrations:

```powershell
dotnet tool run dotnet-ef database update `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext
```

Apply or roll back the local database to a specific migration:

```powershell
dotnet tool run dotnet-ef database update <MigrationName> `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext
```

Use `0` as the target only when you intentionally want to roll back every migration in a disposable local database.

### 7.5 Remove the latest unshared migration

If the latest migration is incorrect and has not been shared or applied outside your disposable local environment:

1. Roll the local database back to the previous migration if necessary.
2. Remove the latest migration:

```powershell
dotnet tool run dotnet-ef migrations remove `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext
```

Never remove an already shared migration. Add a new migration that corrects it.

### 7.6 Generate a deployment script

Generate an idempotent SQL script for review or controlled deployment:

```powershell
dotnet tool run dotnet-ef migrations script --idempotent `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext `
  --output HarborOps-Migrations.sql
```

Treat generated SQL as an artifact. Review it before execution and do not commit it unless the deployment process explicitly requires it.

### 7.7 Concurrent migration changes

Because the solution has one migration snapshot:

- coordinate when multiple branches change the database model;
- update your branch before generating the migration;
- if the snapshot conflicts, prefer removing and regenerating your unshared migration against the latest target branch;
- do not manually change migration timestamps to force ordering;
- rerun build and tests after resolving migration conflicts.

---

## 8. Testing approach

### Unit tests

Use `tests/HarborOps.UnitTests` for focused business and application behavior. Tests must be fast and should not require the shared SQL Server.

### Integration tests

Use `tests/HarborOps.IntegrationTests` for HTTP behavior, controller wiring, serialization, dependency injection, and persistence integration. The current API test factory replaces SQL Server with an isolated EF Core in-memory database.

### Architecture tests

Use `tests/HarborOps.ArchitectureTests` to enforce structural rules. The current tests verify that modules do not reference other modules and that Infrastructure discovers all module assemblies correctly.

Run a specific test project while developing:

```powershell
dotnet test tests/HarborOps.UnitTests/HarborOps.UnitTests.csproj
dotnet test tests/HarborOps.IntegrationTests/HarborOps.IntegrationTests.csproj
dotnet test tests/HarborOps.ArchitectureTests/HarborOps.ArchitectureTests.csproj
```

---

## 9. Pull-request checklist

Before requesting review, confirm:

- [ ] The feature is in the correct business module.
- [ ] No module-to-module project reference was added.
- [ ] Cross-module relationships use scalar IDs.
- [ ] New tables use the owning module's SQL schema.
- [ ] Implementation types are internal unless intentionally public.
- [ ] Business rules are not located in controllers or the composition root.
- [ ] Unit and integration tests cover the important behavior.
- [ ] Database changes include one reviewed global migration.
- [ ] No secrets or developer-specific settings are committed.
- [ ] Restore, build, tests, and format verification pass.
- [ ] The change is focused and does not introduce unrequested architecture.

---

## 10. Useful commands

```powershell
# Restore tools and packages
dotnet tool restore
dotnet restore HarborOps.slnx

# Build
dotnet build HarborOps.slnx --no-restore

# Test
dotnet test HarborOps.slnx --no-build

# Verify formatting
dotnet format HarborOps.slnx --verify-no-changes --no-restore

# Run the API
dotnet run --project src/HarborOps.Api

# Run the Web application
dotnet run --project src/HarborOps.Web

# List migrations
dotnet tool run dotnet-ef migrations list `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext

# Apply pending migrations
dotnet tool run dotnet-ef database update `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext
```

---

## 11. Definition of done

A HarborOps change is done when it:

1. implements the requested business behavior in the correct module;
2. respects module and persistence boundaries;
3. includes appropriate automated tests;
4. includes a reviewed migration when the data model changes;
5. passes restore, build, tests, and format verification;
6. contains no secrets or unrelated architectural changes;
7. is understandable to the next developer without hidden setup steps.

