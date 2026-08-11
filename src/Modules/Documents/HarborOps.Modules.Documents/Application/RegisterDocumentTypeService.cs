using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

public sealed class RegisterDocumentTypeService(DbContext dbContext)
{
    public async Task<RegisterDocumentTypeResponse> RegisterAsync(
        RegisterDocumentTypeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RegisterDocumentTypeValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RegisterDocumentTypeValidationException(errors);
        }

        var name = request.Name!.Trim();
        var mandatoryValidityPeriod = request.MandatoryValidityPeriodDays!.Value;
        var issuingAuthority = request.IssuingAuthority!.Trim();
        var requiresSurvey = request.RequiresSurvey!.Value;

        var documentTypes = dbContext.Set<DocumentType>();

        // Projected and compared in memory rather than via a translated
        // Where(x => x.Name.ToUpper() == ...), so duplicate detection doesn't depend on
        // SQL Server vs. the in-memory provider's collation/case-sensitivity differences.
        var existingNames = await documentTypes
            .AsNoTracking()
            .Select(documentType => documentType.Name)
            .ToListAsync(cancellationToken);

        var normalizedName = name.ToUpperInvariant();
        var nameExists = existingNames.Any(
            existingName => existingName.Trim().ToUpperInvariant() == normalizedName);

        if (nameExists)
        {
            throw new DuplicateDocumentTypeNameException(name);
        }

        var documentType = DocumentType.Register(
            name,
            mandatoryValidityPeriod,
            issuingAuthority,
            requiresSurvey);

        await documentTypes.AddAsync(documentType, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterDocumentTypeResponse(documentType.Id);
    }
}
