# HarborOps

HarborOps is a professional maritime operations management system organized as a .NET 10 modular monolith.

## Solution

- `HarborOps.Api` is the ASP.NET Core API and composition root.
- `HarborOps.Web` is the separately hosted Blazor Web frontend.
- `HarborOps.Infrastructure` owns SQL Server integration, `HarborOpsDbContext`, and the global EF Core migration sequence.
- `HarborOps.SharedKernel` is reserved for small, stable primitives genuinely shared across modules.
- `src/Modules` contains one project per business capability.

The initial modules are Fleet, Crew, Voyages, Cargo, Maintenance, Compliance, Documents, and Identity.

## Module layout

Each module project contains `Domain`, `Application`, `Infrastructure`, `Contracts`, and `Presentation` folders. Modules do not reference one another. Cross-module relationships use scalar identifiers and explicit contracts rather than EF Core navigation properties.

Each module exposes a public module marker. `HarborOps.Infrastructure` uses those markers to discover internal `IEntityTypeConfiguration<T>` implementations while keeping implementation types non-public.

## Data

HarborOps starts with one `HarborOpsDbContext` and one migration sequence. Module tables use these SQL schemas:

| Module | Schema |
| --- | --- |
| Fleet | `fleet` |
| Crew | `crew` |
| Voyages | `voyages` |
| Cargo | `cargo` |
| Maintenance | `maintenance` |
| Compliance | `compliance` |
| Documents | `documents` |
| Identity | `identity` |

Create future migrations from the repository root:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations add <MigrationName> `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext `
  --output-dir Persistence/Migrations
```

## Local validation

```powershell
dotnet restore HarborOps.slnx
dotnet build HarborOps.slnx --no-restore
dotnet test HarborOps.slnx --no-build
dotnet format HarborOps.slnx --verify-no-changes --no-restore
```
