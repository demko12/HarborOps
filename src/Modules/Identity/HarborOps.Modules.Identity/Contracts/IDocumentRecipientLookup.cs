namespace HarborOps.Modules.Identity.Contracts;

/// <summary>Minimal cross-module identity read model for document distribution.</summary>
public interface IDocumentRecipientLookup
{
    Task<IReadOnlyList<DocumentRecipientAccount>> LookupAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentRecipientAccount>> SearchAsync(string? search, int take, CancellationToken cancellationToken);
}

public sealed record DocumentRecipientAccount(Guid UserId, string Email, bool IsActive, bool CanReadDocuments);
