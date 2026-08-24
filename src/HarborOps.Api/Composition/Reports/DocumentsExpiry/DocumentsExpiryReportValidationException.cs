namespace HarborOps.Api.Composition.Reports.DocumentsExpiry;

public sealed class DocumentsExpiryReportValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Documents expiry report validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
