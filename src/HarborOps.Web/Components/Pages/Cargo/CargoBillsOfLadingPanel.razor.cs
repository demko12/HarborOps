using System.ComponentModel.DataAnnotations;
using HarborOps.Web.Api.Cargo;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace HarborOps.Web.Components.Pages.Cargo;

public partial class CargoBillsOfLadingPanel : IDisposable
{
    private const string IssueAction = "issue";
    private const string VoidAction = "void";

    private static readonly IReadOnlyDictionary<string, string> FieldMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["billNumber"] = nameof(BillFormModel.BillNumber),
            ["shipperName"] = nameof(BillFormModel.ShipperName),
            ["shipperAddress"] = nameof(BillFormModel.ShipperAddress),
            ["consigneeName"] = nameof(BillFormModel.ConsigneeName),
            ["consigneeAddress"] = nameof(BillFormModel.ConsigneeAddress),
            ["notifyPartyName"] = nameof(BillFormModel.NotifyPartyName),
            ["notifyPartyAddress"] = nameof(BillFormModel.NotifyPartyAddress),
            ["placeOfReceipt"] = nameof(BillFormModel.PlaceOfReceipt),
            ["portOfLoading"] = nameof(BillFormModel.PortOfLoading),
            ["portOfDischarge"] = nameof(BillFormModel.PortOfDischarge),
            ["placeOfDelivery"] = nameof(BillFormModel.PlaceOfDelivery),
            ["freightTerms"] = nameof(BillFormModel.FreightTerms),
            ["cargoMarks"] = nameof(BillFormModel.CargoMarks),
            ["cargoDescription"] = nameof(BillFormModel.CargoDescription),
            ["packageCount"] = nameof(BillFormModel.PackageCount),
            ["weightKg"] = nameof(BillFormModel.WeightKg),
            ["issuePlace"] = nameof(BillFormModel.IssuePlace),
            ["requestedIssueDate"] = nameof(BillFormModel.RequestedIssueDate),
        };

    private readonly CancellationTokenSource disposeCancellation = new();

    private BillOfLadingList? list;
    private CargoApiFailure? failure;
    private bool isLoading = true;
    private Guid loadedBookingId;

    private BillOfLadingView? selected;
    private CargoApiFailure? selectedFailure;
    private bool isLoadingSelected;
    private Guid selectedBillId;

    private BillFormModel formModel = new();
    private EditContext formEditContext = null!;
    private ValidationMessageStore formMessages = null!;
    private HashSet<Guid> selectedContainerIds = [];
    private bool isEditing;
    private bool isCreating;
    private bool isSaving;
    private string? formError;

    private string? pendingAction;
    private bool isActing;
    private string? actionError;
    private string? voidReason;
    private string? voidReasonError;

    private bool isHistoryOpen;

    [Parameter]
    public Guid BookingId { get; set; }

    [Parameter]
    public EventCallback OnChanged { get; set; }

    private IReadOnlyList<CargoContainerRow> Containers { get; set; } = [];

    private bool CanManage => AuthorizationState.CanManageCargo;

    private IReadOnlyList<BillOfLadingListRow> Bills => list?.BillsOfLading ?? [];

    private BillOfLadingRow? Bill => selected?.BillOfLading;

    private IReadOnlyList<BillOfLadingEventRow> History => selected?.History ?? [];

    private bool ShowForm => isCreating || isEditing;

    private IReadOnlyList<string> KnownPorts =>
    [
        .. Bills
            .SelectMany(bill => new[] { bill.PortOfLoading, bill.PortOfDischarge })
            .Where(port => !string.IsNullOrWhiteSpace(port))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal),
    ];

    private int SelectedContainerCount => selectedContainerIds.Count;

    private decimal SelectedWeightKg => formModel.WeightKg ?? 0m;

    private int SelectedPackageCount => formModel.PackageCount ?? 0;

    private string ConfirmationQuestion => pendingAction switch
    {
        IssueAction =>
            "Issue this bill of lading? Once issued it can no longer be edited, only voided.",
        VoidAction =>
            "Void this bill of lading? The issued record is kept and its containers are released.",
        _ => string.Empty,
    };

    private string ConfirmLabel => pendingAction switch
    {
        IssueAction => "Yes, issue",
        VoidAction => "Yes, void",
        _ => string.Empty,
    };

    public void Dispose()
    {
        DetachValidation();

        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    protected override void OnInitialized()
    {
        ResetForm();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (loadedBookingId == BookingId && !isLoading)
        {
            return;
        }

        await LoadListAsync();
    }

    private async Task LoadListAsync()
    {
        isLoading = true;
        failure = null;
        loadedBookingId = BookingId;

        try
        {
            var result = await CargoApi.GetBillsOfLadingAsync(BookingId, disposeCancellation.Token);

            if (result.IsSuccess)
            {
                list = result.Value;
            }
            else
            {
                list = null;
                failure = result.Failure;
            }

            var containerResult = await CargoApi.GetContainersAsync(
                BookingId,
                disposeCancellation.Token);

            Containers = containerResult.IsSuccess ? containerResult.Value!.Items : [];
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task SelectBillAsync(Guid billOfLadingId)
    {
        if (selectedBillId == billOfLadingId && selected is not null)
        {
            CloseBill();

            return;
        }

        isCreating = false;
        isEditing = false;
        selectedBillId = billOfLadingId;

        await LoadSelectedAsync();
    }

    private async Task LoadSelectedAsync()
    {
        isLoadingSelected = true;
        selectedFailure = null;

        try
        {
            var result = await CargoApi.GetBillOfLadingAsync(
                selectedBillId,
                disposeCancellation.Token);

            if (result.IsSuccess)
            {
                selected = result.Value;
            }
            else
            {
                selected = null;
                selectedFailure = result.Failure;
            }
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isLoadingSelected = false;
        }
    }

    private void CloseBill()
    {
        selected = null;
        selectedFailure = null;
        selectedBillId = Guid.Empty;
        isCreating = false;
        isEditing = false;
        isHistoryOpen = false;
    }

    private void StartCreate()
    {
        CloseBill();
        ResetForm();
        selectedContainerIds = [];
        formError = null;
        isCreating = true;
    }

    private void StartReplacement()
    {
        if (Bill is not { } voided)
        {
            return;
        }

        CopyInto(voided, keepBillNumber: false);
        selectedContainerIds = [.. voided.Containers.Select(container => container.ContainerId)];
        formError = null;
        isEditing = false;
        isCreating = true;
    }

    private void StartEdit()
    {
        if (Bill is not { } bill)
        {
            return;
        }

        CopyInto(bill, keepBillNumber: true);
        selectedContainerIds = [.. bill.Containers.Select(container => container.ContainerId)];
        formError = null;
        isEditing = true;
    }

    private void CopyInto(BillOfLadingRow bill, bool keepBillNumber)
    {
        ResetForm();
        formModel = new BillFormModel
        {
            BillNumber = keepBillNumber ? bill.BillNumber : null,
            ShipperName = bill.ShipperName,
            ShipperAddress = bill.ShipperAddress,
            ConsigneeName = bill.ConsigneeName,
            ConsigneeAddress = bill.ConsigneeAddress,
            NotifyPartyName = bill.NotifyPartyName,
            NotifyPartyAddress = bill.NotifyPartyAddress,
            PlaceOfReceipt = bill.PlaceOfReceipt,
            PortOfLoading = bill.PortOfLoading,
            PortOfDischarge = bill.PortOfDischarge,
            PlaceOfDelivery = bill.PlaceOfDelivery,
            FreightTerms = bill.FreightTerms,
            CargoMarks = bill.CargoMarks,
            CargoDescription = bill.CargoDescription,
            PackageCount = bill.PackageCount,
            WeightKg = bill.WeightKg,
            IssuePlace = bill.IssuePlace,
            RequestedIssueDate = bill.RequestedIssueDate,
        };

        AttachValidation();
    }

    private void CancelForm()
    {
        if (isSaving)
        {
            return;
        }

        isCreating = false;
        isEditing = false;
        formError = null;
        ResetForm();
    }

    private void ToggleContainer(Guid containerId, bool isSelected)
    {
        if (isSelected)
        {
            selectedContainerIds.Add(containerId);
        }
        else
        {
            selectedContainerIds.Remove(containerId);
        }
    }

    private bool IsContainerSelected(Guid containerId)
    {
        return selectedContainerIds.Contains(containerId);
    }

    private async Task SubmitFormAsync()
    {
        if (isSaving)
        {
            return;
        }

        formMessages.Clear();
        formError = null;

        if (!formEditContext.Validate())
        {
            return;
        }

        isSaving = true;

        try
        {
            var request = new SaveBillOfLadingWebRequest(
                formModel.BillNumber!.Trim(),
                formModel.ShipperName!.Trim(),
                formModel.ShipperAddress!.Trim(),
                formModel.ConsigneeName!.Trim(),
                formModel.ConsigneeAddress!.Trim(),
                Optional(formModel.NotifyPartyName),
                Optional(formModel.NotifyPartyAddress),
                formModel.PlaceOfReceipt!.Trim(),
                formModel.PortOfLoading!.Trim(),
                formModel.PortOfDischarge!.Trim(),
                formModel.PlaceOfDelivery!.Trim(),
                formModel.FreightTerms!,
                formModel.CargoMarks!.Trim(),
                formModel.CargoDescription!.Trim(),
                formModel.PackageCount!.Value,
                formModel.WeightKg!.Value,
                formModel.IssuePlace!.Trim(),
                formModel.RequestedIssueDate!.Value,
                [.. selectedContainerIds]);

            var result = isCreating
                ? await CargoApi.CreateBillOfLadingAsync(
                    BookingId,
                    request,
                    disposeCancellation.Token)
                : await CargoApi.UpdateBillOfLadingAsync(
                    selectedBillId,
                    request,
                    disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                CargoFormFailure.Apply(
                    result.Failure!,
                    formEditContext,
                    formMessages,
                    FieldMap,
                    message => formError = message);

                if (result.Failure!.IsConflict || result.Failure!.IsNotFound)
                {
                    await LoadListAsync();
                    await OnChanged.InvokeAsync();
                }

                return;
            }

            selectedBillId = result.Value!.BillOfLadingId;
            isCreating = false;
            isEditing = false;
            ResetForm();

            await LoadListAsync();
            await LoadSelectedAsync();
            await OnChanged.InvokeAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isSaving = false;
        }
    }

    private void AskToConfirm(string action)
    {
        pendingAction = action;
        actionError = null;
        voidReason = null;
        voidReasonError = null;
    }

    private void DismissConfirmation()
    {
        if (isActing)
        {
            return;
        }

        pendingAction = null;
        actionError = null;
        voidReason = null;
        voidReasonError = null;
    }

    private async Task ConfirmPendingActionAsync()
    {
        if (isActing || pendingAction is not { } action)
        {
            return;
        }

        var reason = string.IsNullOrWhiteSpace(voidReason) ? null : voidReason.Trim();

        if (action == VoidAction && reason is null)
        {
            voidReasonError = "A void reason is required.";

            return;
        }

        isActing = true;
        actionError = null;
        voidReasonError = null;

        try
        {
            var result = action == IssueAction
                ? await CargoApi.IssueBillOfLadingAsync(selectedBillId, disposeCancellation.Token)
                : await CargoApi.VoidBillOfLadingAsync(
                    selectedBillId,
                    new VoidBillOfLadingWebRequest(reason!),
                    disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                var actionFailure = result.Failure!;

                if (actionFailure.Errors.TryGetValue("reason", out var reasonMessages))
                {
                    voidReasonError = string.Join(" ", reasonMessages);
                }
                else
                {
                    actionError = $"{actionFailure.Title} {actionFailure.Message}".Trim();
                }

                if (actionFailure.IsConflict || actionFailure.IsNotFound)
                {
                    await LoadListAsync();
                    await LoadSelectedAsync();
                    await OnChanged.InvokeAsync();
                }

                return;
            }

            pendingAction = null;
            voidReason = null;

            await LoadListAsync();
            await LoadSelectedAsync();
            await OnChanged.InvokeAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isActing = false;
        }
    }

    private void ShowHistory()
    {
        isHistoryOpen = true;
    }

    private void HideHistory()
    {
        isHistoryOpen = false;
    }

    private string PrintHref(Guid billOfLadingId)
    {
        return $"/cargo/{BookingId}/bills-of-lading/{billOfLadingId}/print";
    }

    private static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private void ResetForm()
    {
        DetachValidation();

        formModel = new BillFormModel();
        AttachValidation();
    }

    private void AttachValidation()
    {
        formEditContext = new EditContext(formModel);
        formMessages = new ValidationMessageStore(formEditContext);
        formEditContext.OnValidationRequested += HandleValidationRequested;
    }

    private void DetachValidation()
    {
        if (formEditContext is not null)
        {
            formEditContext.OnValidationRequested -= HandleValidationRequested;
        }
    }

    private void HandleValidationRequested(object? sender, ValidationRequestedEventArgs args)
    {
        formMessages.Clear();
    }

    private sealed class BillFormModel
    {
        [Required(ErrorMessage = "A bill number is required.")]
        [StringLength(30, ErrorMessage = "Bill number must not exceed 30 characters.")]
        public string? BillNumber { get; set; }

        [Required(ErrorMessage = "A shipper name is required.")]
        [StringLength(200, ErrorMessage = "Shipper name must not exceed 200 characters.")]
        public string? ShipperName { get; set; }

        [Required(ErrorMessage = "A shipper address is required.")]
        [StringLength(500, ErrorMessage = "Shipper address must not exceed 500 characters.")]
        public string? ShipperAddress { get; set; }

        [Required(ErrorMessage = "A consignee name is required.")]
        [StringLength(200, ErrorMessage = "Consignee name must not exceed 200 characters.")]
        public string? ConsigneeName { get; set; }

        [Required(ErrorMessage = "A consignee address is required.")]
        [StringLength(500, ErrorMessage = "Consignee address must not exceed 500 characters.")]
        public string? ConsigneeAddress { get; set; }

        [StringLength(200, ErrorMessage = "Notify party name must not exceed 200 characters.")]
        public string? NotifyPartyName { get; set; }

        [StringLength(500, ErrorMessage = "Notify party address must not exceed 500 characters.")]
        public string? NotifyPartyAddress { get; set; }

        [Required(ErrorMessage = "A place of receipt is required.")]
        [StringLength(200, ErrorMessage = "Place of receipt must not exceed 200 characters.")]
        public string? PlaceOfReceipt { get; set; }

        [Required(ErrorMessage = "A port of loading is required.")]
        [StringLength(200, ErrorMessage = "Port of loading must not exceed 200 characters.")]
        public string? PortOfLoading { get; set; }

        [Required(ErrorMessage = "A port of discharge is required.")]
        [StringLength(200, ErrorMessage = "Port of discharge must not exceed 200 characters.")]
        public string? PortOfDischarge { get; set; }

        [Required(ErrorMessage = "A place of delivery is required.")]
        [StringLength(200, ErrorMessage = "Place of delivery must not exceed 200 characters.")]
        public string? PlaceOfDelivery { get; set; }

        [Required(ErrorMessage = "Freight terms are required.")]
        public string? FreightTerms { get; set; } = "Prepaid";

        [Required(ErrorMessage = "Cargo marks are required.")]
        [StringLength(500, ErrorMessage = "Cargo marks must not exceed 500 characters.")]
        public string? CargoMarks { get; set; }

        [Required(ErrorMessage = "A cargo description is required.")]
        [StringLength(1000, ErrorMessage = "Cargo description must not exceed 1000 characters.")]
        public string? CargoDescription { get; set; }

        [Required(ErrorMessage = "A package count is required.")]
        [Range(1, 1_000_000, ErrorMessage = "Package count must be between 1 and 1000000.")]
        public int? PackageCount { get; set; }

        [Required(ErrorMessage = "A weight is required.")]
        [Range(0.001, 1_000_000_000, ErrorMessage = "Weight must be greater than zero.")]
        public decimal? WeightKg { get; set; }

        [Required(ErrorMessage = "An issue place is required.")]
        [StringLength(200, ErrorMessage = "Issue place must not exceed 200 characters.")]
        public string? IssuePlace { get; set; }

        [Required(ErrorMessage = "A requested issue date is required.")]
        public DateOnly? RequestedIssueDate { get; set; } =
            DateOnly.FromDateTime(DateTime.UtcNow);
    }
}
