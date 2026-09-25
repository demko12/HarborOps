using HarborOps.Web.Api.Documents;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Documents;

public partial class DocumentsReadiness
{
    private readonly CancellationTokenSource disposeCancellation = new();

    private IReadOnlyList<DocumentTypeOption> documentTypeOptions = [];
    private bool isLoadingDocumentTypes = true;

    private int readinessRefreshToken;
    private Guid? expandedDocumentId;
    private (Guid VesselId, string VesselName, int DocumentTypeId)? missingDocument;

    [SupplyParameterFromQuery(Name = "vesselId")]
    private Guid? RequestedVesselId { get; set; }

    protected override async Task OnInitializedAsync()
    {
        DocumentsApiResult<DocumentTypesPage> result;

        try
        {
            result = await DocumentsApi.GetDocumentTypesAsync(
                new GetDocumentTypesQuery(null, Documents.DocumentTypeOptionsPageSize),
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isLoadingDocumentTypes = false;
        documentTypeOptions = result.IsSuccess
            ? result.Value!.Items.Select(type => new DocumentTypeOption(type.DocumentTypeId, type.Name, type.MandatoryValidityPeriodDays)).ToList()
            : [];
    }

    private void OpenFilePanel(Guid documentId) => expandedDocumentId = documentId;

    private void CloseFilePanel() => expandedDocumentId = null;

    private void OpenRegisterFormForMissing((Guid VesselId, string VesselName, int DocumentTypeId) missing) =>
        missingDocument = missing;

    private void CloseRegisterDocumentForm() => missingDocument = null;

    private void RefreshReadiness() => readinessRefreshToken++;

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
