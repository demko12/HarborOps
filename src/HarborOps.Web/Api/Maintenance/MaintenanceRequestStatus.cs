namespace HarborOps.Web.Api.Maintenance;

internal enum MaintenanceRequestStatus
{
    Success,
    ValidationFailed,
    Conflict,
    NotFound,
    Error,
}
