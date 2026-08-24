namespace HarborOps.Modules.Fleet.Application;

internal sealed class FleetNotFoundException : Exception;
internal sealed class FleetAssignmentVesselNotFoundException : Exception;
internal sealed class DuplicateFleetNameException : Exception;
internal sealed class VesselAlreadyAssignedToFleetException : Exception;
internal sealed class NoActiveFleetAssignmentException : Exception;
