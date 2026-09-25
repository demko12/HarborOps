namespace HarborOps.Modules.Documents.Application;

internal sealed class DocumentRequirementValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Document requirement validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class DocumentRequirementNotFoundException(Guid requirementId)
    : Exception($"Document requirement '{requirementId}' was not found.")
{
    internal Guid RequirementId { get; } = requirementId;
}

internal sealed class DuplicateDocumentRequirementException(Guid vesselId, int documentTypeId, Exception? innerException = null)
    : Exception(
        $"Vessel '{vesselId}' already has a requirement for document type '{documentTypeId}'.",
        innerException)
{
    internal Guid VesselId { get; } = vesselId;
    internal int DocumentTypeId { get; } = documentTypeId;
}

internal sealed class DocumentRequirementConflictException(Guid requirementId, Exception? innerException = null)
    : Exception(
        $"Document requirement '{requirementId}' was changed by another request. Reload and try again.",
        innerException)
{
    internal Guid RequirementId { get; } = requirementId;
}

internal sealed class DocumentRequirementVesselNotFoundException(Guid vesselId)
    : Exception($"Vessel with ID '{vesselId}' was not found.")
{
    internal Guid VesselId { get; } = vesselId;
}

internal sealed class GetDocumentRequirementsValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get document requirements validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class GetDocumentReadinessValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get document readiness validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
