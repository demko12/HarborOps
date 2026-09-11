using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

public sealed class RetentionPolicyAdminService(DbContext dbContext)
{
    internal const string RetentionPeriodDaysField = "retentionPeriodDays";

    public async Task UpdatePolicyAsync(int documentTypeId, UpdateRetentionPolicyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var retentionPeriodDays = Validate(request.RetentionPeriodDays);

        var typeExists = await dbContext.Set<DocumentType>().AsNoTracking().AnyAsync(type => type.Id == documentTypeId, cancellationToken);
        if (!typeExists)
        {
            throw new DocumentTypeNotFoundException(documentTypeId);
        }

        var policy = await dbContext.Set<RetentionPolicy>().SingleOrDefaultAsync(p => p.DocumentTypeId == documentTypeId, cancellationToken);

        if (policy is null)
        {
            await dbContext.Set<RetentionPolicy>().AddAsync(new RetentionPolicy(documentTypeId, retentionPeriodDays), cancellationToken);
        }
        else
        {
            policy.UpdateRetentionPeriod(retentionPeriodDays);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static int Validate(int? retentionPeriodDays)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (retentionPeriodDays is null or <= 0)
        {
            errors[RetentionPeriodDaysField] = ["Retention period must be greater than zero days."];
        }

        if (errors.Count > 0)
        {
            throw new RetentionPolicyValidationException(errors);
        }

        return retentionPeriodDays!.Value;
    }
}
