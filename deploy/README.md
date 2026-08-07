# Deployment

Deployment assets will live here when a deployment target is selected.

## Migration History

The empty `20260806095922_UpdateCrewAssignment` migration is present in the
`__EFMigrationsHistory` table of the shared development database as of
2026-08-07. It must remain in the global migration sequence even though its
`Up` and `Down` methods contain no operations.

Before removing any migration, verify its status in every shared environment.
Never remove or rewrite a migration that has already been applied.
