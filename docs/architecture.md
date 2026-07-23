# Architecture

HarborOps is a modular monolith. The API composes business modules into one deployable backend, while the Blazor frontend runs as a separate web application and communicates with the API over HTTP.

## Dependency direction

```text
HarborOps.Api
├── HarborOps.Infrastructure
└── Business modules

HarborOps.Infrastructure
└── Business modules

Business modules
└── HarborOps.SharedKernel
```

There are no module-to-module project references. `HarborOps.Web` has no backend project references.

## Persistence

`HarborOpsDbContext` is the single EF Core context. It applies entity configurations from each module assembly through that module's public marker type. Configuration implementations may remain internal.

All migrations belong to `HarborOps.Infrastructure/Persistence/Migrations`. A migration may include changes from multiple modules, preserving one ordered migration history for the application.

Each module owns a SQL schema. Cross-module references are represented by scalar identifiers and do not use EF Core navigation properties.

## Module boundaries

Each module contains:

- `Domain` for business rules and domain models.
- `Application` for use-case orchestration.
- `Infrastructure` for module-specific persistence mappings and adapters.
- `Contracts` for intentional integration and API contracts.
- `Presentation` for API endpoint composition.

These folders are organizational boundaries inside one project per module. Additional projects should be introduced only when module scale or enforcement needs justify them.
