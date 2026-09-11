using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Documents.Application;

internal sealed class GetRetentionPoliciesQuery(DbContext dbContext)
    : IGetRetentionPoliciesQuery
{
    public async Task<GetRetentionPoliciesResponse> GetRetentionPoliciesAsync(CancellationToken cancellationToken)
    {
        var items = await (
            from policy in dbContext.Set<RetentionPolicy>().AsNoTracking()
            join documentType in dbContext.Set<DocumentType>().AsNoTracking()
                on policy.DocumentTypeId equals documentType.Id
            orderby policy.DocumentTypeId
            select new RetentionPolicyListItem(policy.DocumentTypeId, documentType.Name, policy.RetentionPeriodDays))
            .ToListAsync(cancellationToken);

        return new GetRetentionPoliciesResponse(items);
    }
}
