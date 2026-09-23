namespace HarborOps.Modules.Compliance.Application;

internal sealed class PscInspectionWorkflowValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("The PSC inspection workflow request is invalid.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class PscInspectionWorkflowConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
