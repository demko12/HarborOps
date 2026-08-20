using System.ComponentModel.DataAnnotations;
using HarborOps.Web.Api.Cargo;
using HarborOps.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace HarborOps.Web.Components.Pages.Cargo;

public partial class CargoBookings
{
    private const int DefaultPageSize = 25;

    private readonly CancellationTokenSource disposeCancellation = new();
    private int loadVersion;
    private IReadOnlyList<CargoBookingRow> rows = [];
    private CargoApiFailure? failure;
    private string? search;
    private string status = string.Empty;
    private bool hazardousOnly;
    private int pageNumber = 1;
    private int pageSize = DefaultPageSize;
    private int totalCount;
    private bool isLoading = true;

    [SupplyParameterFromQuery(Name = SearchQueryKey)]
    public string? SearchFromQuery { get; set; }

    [SupplyParameterFromQuery(Name = StatusQueryKey)]
    public string? StatusFromQuery { get; set; }

    [SupplyParameterFromQuery(Name = HazardousQueryKey)]
    public bool? HazardousFromQuery { get; set; }

    [SupplyParameterFromQuery(Name = PageQueryKey)]
    public int? PageFromQuery { get; set; }

    [SupplyParameterFromQuery(Name = SizeQueryKey)]
    public int? SizeFromQuery { get; set; }

    private const string SearchQueryKey = "search";
    private const string StatusQueryKey = "status";
    private const string HazardousQueryKey = "hazardous";
    private const string PageQueryKey = "page";
    private const string SizeQueryKey = "size";

    private string BoardQuery
    {
        get
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(search))
            {
                parts.Add($"{SearchQueryKey}={Uri.EscapeDataString(search.Trim())}");
            }

            if (!string.IsNullOrEmpty(status))
            {
                parts.Add($"{StatusQueryKey}={Uri.EscapeDataString(status)}");
            }

            if (hazardousOnly)
            {
                parts.Add($"{HazardousQueryKey}=true");
            }

            if (pageNumber > 1)
            {
                parts.Add($"{PageQueryKey}={pageNumber}");
            }

            if (pageSize != DefaultPageSize)
            {
                parts.Add($"{SizeQueryKey}={pageSize}");
            }

            return string.Join('&', parts);
        }
    }

    private string DetailsHref(Guid cargoBookingId)
    {
        var query = BoardQuery;

        return query.Length == 0
            ? $"/cargo/{cargoBookingId}"
            : $"/cargo/{cargoBookingId}?from={Uri.EscapeDataString(query)}";
    }

    private void SyncBoardAddress()
    {
        var query = BoardQuery;
        var target = query.Length == 0 ? "/cargo" : $"/cargo?{query}";

        if (string.Equals(
            Navigation.ToBaseRelativePath(Navigation.Uri).TrimStart('/'),
            target.TrimStart('/'),
            StringComparison.Ordinal))
        {
            return;
        }

        Navigation.NavigateTo(target, forceLoad: false, replace: true);
    }

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;

    private int FirstRowNumber => rows.Count == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => FirstRowNumber + rows.Count - 1;

    protected override Task OnInitializedAsync()
    {
        ResetRegisterForm();
        ApplyAddressToFilters();

        return LoadAsync();
    }

    private void ApplyAddressToFilters()
    {
        search = string.IsNullOrWhiteSpace(SearchFromQuery) ? null : SearchFromQuery;
        status = StatusFromQuery ?? string.Empty;
        hazardousOnly = HazardousFromQuery == true;
        pageNumber = PageFromQuery is > 0 ? PageFromQuery.Value : 1;
        pageSize = SizeFromQuery is > 0 ? SizeFromQuery.Value : DefaultPageSize;
    }

    private Task ApplyFiltersAsync()
    {
        pageNumber = 1;

        return LoadAsync();
    }

    private Task ClearFiltersAsync()
    {
        search = null;
        status = string.Empty;
        hazardousOnly = false;
        pageNumber = 1;

        return LoadAsync();
    }

    private Task RetryAsync()
    {
        return LoadAsync();
    }

    private Task PreviousPageAsync()
    {
        if (pageNumber <= 1)
        {
            return Task.CompletedTask;
        }

        pageNumber--;

        return LoadAsync();
    }

    private Task NextPageAsync()
    {
        if (pageNumber >= TotalPages)
        {
            return Task.CompletedTask;
        }

        pageNumber++;

        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        var requestVersion = ++loadVersion;
        var cancellationToken = disposeCancellation.Token;
        isLoading = true;
        failure = null;

        var filter = new CargoBookingFilter(
            search,
            string.IsNullOrEmpty(status) ? null : status,
            hazardousOnly ? true : null,
            pageNumber,
            pageSize);

        CargoApiResult<CargoBookingPage> result;
        try
        {
            result = await CargoApi.SearchAsync(filter, cancellationToken);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        isLoading = false;

        if (!result.IsSuccess)
        {
            failure = result.Failure;
            rows = [];
            totalCount = 0;

            return;
        }

        var page = result.Value!;
        rows = page.Items;
        pageNumber = page.PageNumber;
        pageSize = page.PageSize;
        totalCount = page.TotalCount;

        SyncBoardAddress();
    }

    private bool isRegisterOpen;
    private bool isRegistering;
    private string? registerError;
    private RegisterBookingFormModel registerModel = new();
    private EditContext registerEditContext = null!;
    private ValidationMessageStore registerMessages = null!;
    private VoyagePicker? voyagePicker;

    private void OpenRegisterDialog()
    {
        registerError = null;
        isRegisterOpen = true;
    }

    private void CloseRegisterDialog()
    {
        if (isRegistering)
        {
            return;
        }

        ResetRegisterForm();
        registerError = null;
        isRegisterOpen = false;
    }

    private async Task SubmitRegistrationAsync()
    {
        if (isRegistering)
        {
            return;
        }

        registerMessages.Clear();
        registerError = null;

        if (!registerEditContext.Validate())
        {
            return;
        }

        isRegistering = true;

        try
        {
            var request = new RegisterCargoBookingWebRequest(
                registerModel.BookingNumber!.Trim(),
                registerModel.VoyageId!.Value,
                registerModel.Description!.Trim(),
                registerModel.CargoType!.Trim(),
                registerModel.WeightKg!.Value,
                registerModel.PackageCount!.Value,
                registerModel.IsHazardous);

            var result = await CargoApi.RegisterAsync(request, disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                ApplyRegisterFailure(result.Failure!);

                return;
            }

            ResetRegisterForm();
            isRegisterOpen = false;

            // The API just told us which booking was created, so continue on it rather than
            // leaving the operator to find it under whatever filter the board still carries.
            // DetailsHref keeps those filters, so the back link returns to the same view.
            if (result.Value is { } registered)
            {
                Navigation.NavigateTo(DetailsHref(registered.Id));

                return;
            }

            await LoadAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isRegistering = false;
        }
    }

    private void ApplyRegisterFailure(CargoApiFailure failure)
    {
        var unmappedMessages = new List<string>();

        foreach (var entry in failure.Errors)
        {
            if (!RegisterFieldMap.TryGetValue(entry.Key, out var property))
            {
                unmappedMessages.AddRange(entry.Value);

                continue;
            }

            registerMessages.Add(new FieldIdentifier(registerEditContext.Model, property), entry.Value);
        }

        if (unmappedMessages.Count > 0)
        {
            registerError = string.Join(" ", unmappedMessages);
        }
        else if (failure.Errors.Count == 0)
        {
            registerError = failure.Title;
        }

        registerEditContext.NotifyValidationStateChanged();
    }

    private void ResetRegisterForm()
    {
        registerModel = new RegisterBookingFormModel();
        registerEditContext = new EditContext(registerModel);
        registerMessages = new ValidationMessageStore(registerEditContext);
        voyagePicker?.Reset();
    }

    private static readonly IReadOnlyDictionary<string, string> RegisterFieldMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bookingNumber"] = nameof(RegisterBookingFormModel.BookingNumber),
            ["voyageId"] = nameof(RegisterBookingFormModel.VoyageId),
            ["description"] = nameof(RegisterBookingFormModel.Description),
            ["cargoType"] = nameof(RegisterBookingFormModel.CargoType),
            ["weightKg"] = nameof(RegisterBookingFormModel.WeightKg),
            ["packageCount"] = nameof(RegisterBookingFormModel.PackageCount),
            ["isHazardous"] = nameof(RegisterBookingFormModel.IsHazardous),
        };

    private sealed class RegisterBookingFormModel
    {
        [Required(ErrorMessage = "Booking number is required.")]
        [StringLength(50, ErrorMessage = "Booking number must not exceed 50 characters.")]
        public string? BookingNumber { get; set; }

        [Required(ErrorMessage = "Select the voyage this cargo sails on.")]
        public Guid? VoyageId { get; set; }

        [Required(ErrorMessage = "Description is required.")]
        [StringLength(500, ErrorMessage = "Description must not exceed 500 characters.")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Cargo type is required.")]
        [StringLength(100, ErrorMessage = "Cargo type must not exceed 100 characters.")]
        public string? CargoType { get; set; }

        [Required(ErrorMessage = "Weight is required.")]
        [Range(typeof(decimal), "0.001", "79228162514264337593543950335", ErrorMessage = "Weight must be greater than zero.")]
        public decimal? WeightKg { get; set; }

        [Required(ErrorMessage = "Package count is required.")]
        [Range(1, int.MaxValue, ErrorMessage = "Package count must be greater than zero.")]
        public int? PackageCount { get; set; }

        public bool IsHazardous { get; set; }
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
