using DocumentContracts = HarborOps.Modules.Documents.Contracts;
using Identity = HarborOps.Modules.Identity.Contracts;

namespace HarborOps.Api.Composition.Documents;

public sealed class IdentityDocumentDistributionRecipientLookup(Identity.IDocumentRecipientLookup identityLookup)
    : DocumentContracts.IDocumentDistributionRecipientLookup
{
    public async Task<IReadOnlyList<DocumentContracts.DocumentDistributionRecipientIdentity>> LookupAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        (await identityLookup.LookupAsync(userIds, cancellationToken))
            .Select(x => new DocumentContracts.DocumentDistributionRecipientIdentity(x.UserId, x.Email, x.IsActive, x.CanReadDocuments)).ToArray();

    public async Task<IReadOnlyList<DocumentContracts.DocumentDistributionRecipientIdentity>> SearchAsync(string? search, int take, CancellationToken cancellationToken) =>
        (await identityLookup.SearchAsync(search, take, cancellationToken))
            .Select(x => new DocumentContracts.DocumentDistributionRecipientIdentity(x.UserId, x.Email, x.IsActive, x.CanReadDocuments)).ToArray();
}
