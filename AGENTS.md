# HarborOps Repository Guidance

## Architecture

- Keep HarborOps as a modular monolith.
- Keep business capabilities isolated in their module projects.
- Do not add project references between business modules.
- Use scalar identifiers rather than cross-module EF Core navigation properties.
- Keep the single `HarborOpsDbContext` and global migration sequence in `HarborOps.Infrastructure`.
- Map each module's tables to its dedicated SQL schema.

## Implementation

- Target .NET 10 and keep nullable reference types enabled.
- Prefer direct, capability-specific code over generic abstractions.
- Do not introduce MediatR, generic repositories, event sourcing, message brokers, or microservices unless explicitly requested.
- Keep module implementation types internal unless they form an intentional public contract.
- Add project and package references only when current code requires them.

## Validation

- Run restore, build, tests, and format verification before handing off changes.
- Do not create migrations unless the task explicitly requires one.
