using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using HarborOps.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

/// <summary>
/// Insurance registration requires a date from today onwards. The policy is then
/// classified against one UTC date captured at the start of each query.
/// </summary>
public sealed class InsurancePolicyService(DbContext dbContext, IDateTimeProvider dateTimeProvider)
{
    private const int ExpiringSoonDays = 30;

    public async Task<RegisterInsurancePolicyResponse> RegisterAsync(
        RegisterInsurancePolicyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var today = DateOnly.FromDateTime(dateTimeProvider.UtcNow);
        var errors = ValidateRegistration(request, today);
        if (errors.Count > 0)
        {
            throw new InsurancePolicyValidationException(errors);
        }

        var insurer = request.Insurer!.Trim();
        var policyNumber = request.PolicyNumber!.Trim();
        var policies = dbContext.Set<InsurancePolicy>();

        if (await policies.AnyAsync(
                policy => policy.Insurer == insurer && policy.PolicyNumber == policyNumber,
                cancellationToken))
        {
            throw new DuplicateInsurancePolicyException();
        }

        var policy = new InsurancePolicy(
            request.VesselId!.Value,
            insurer,
            policyNumber,
            request.Coverage!.Trim(),
            request.ExpiryDate!.Value);

        await policies.AddAsync(policy, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterInsurancePolicyResponse(policy.Id);
    }

    public async Task<GetInsurancePoliciesResponse> GetAsync(
        GetInsurancePoliciesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = ValidateQuery(request);
        if (errors.Count > 0)
        {
            throw new InsurancePolicyValidationException(errors);
        }

        var asOf = DateOnly.FromDateTime(dateTimeProvider.UtcNow);
        var nearExpiry = asOf.AddDays(ExpiringSoonDays);
        var query = dbContext.Set<InsurancePolicy>().AsNoTracking().AsQueryable();

        if (request.VesselId is { } vesselId) query = query.Where(policy => policy.VesselId == vesselId);
        if (!string.IsNullOrWhiteSpace(request.Insurer)) query = query.Where(policy => policy.Insurer.Contains(request.Insurer.Trim()));
        if (!string.IsNullOrWhiteSpace(request.Coverage)) query = query.Where(policy => policy.Coverage.Contains(request.Coverage.Trim()));
        if (request.ExpiryFrom is { } expiryFrom) query = query.Where(policy => policy.ExpiryDate >= expiryFrom);
        if (request.ExpiryTo is { } expiryTo) query = query.Where(policy => policy.ExpiryDate <= expiryTo);
        if (request.ExpiredOnly) query = query.Where(policy => policy.ExpiryDate < asOf);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(policy => policy.ExpiryDate)
            .ThenBy(policy => policy.Id)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(policy => new InsurancePolicyListItem(
                policy.Id,
                policy.VesselId,
                policy.Insurer,
                policy.PolicyNumber,
                policy.Coverage,
                policy.ExpiryDate,
                policy.ExpiryDate < asOf
                    ? "Expired"
                    : policy.ExpiryDate <= nearExpiry
                        ? "Expiring soon"
                        : "Active"))
            .ToListAsync(cancellationToken);

        return new GetInsurancePoliciesResponse(items, totalCount, request.PageNumber, request.PageSize);
    }

    private static Dictionary<string, string[]> ValidateRegistration(RegisterInsurancePolicyRequest request, DateOnly today)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.VesselId is not { } vesselId || vesselId == Guid.Empty) errors["vesselId"] = ["Vessel is required."];
        ValidateText(request.Insurer, InsurancePolicy.InsurerMaxLength, "insurer", "Insurer", errors);
        ValidateText(request.PolicyNumber, InsurancePolicy.PolicyNumberMaxLength, "policyNumber", "Policy number", errors);
        ValidateText(request.Coverage, InsurancePolicy.CoverageMaxLength, "coverage", "Coverage", errors);
        if (request.ExpiryDate is null) errors["expiryDate"] = ["Expiry date is required."];
        else if (request.ExpiryDate < today) errors["expiryDate"] = ["Expiry date must be today or later."];
        return errors;
    }

    private static Dictionary<string, string[]> ValidateQuery(GetInsurancePoliciesRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.PageNumber < 1) errors["pageNumber"] = ["Page number must be at least 1."];
        if (request.PageSize is < 1 or > 100) errors["pageSize"] = ["Page size must be between 1 and 100."];
        if (request.ExpiryFrom is { } from && request.ExpiryTo is { } to && from > to) errors["expiryWindow"] = ["Expiry from must not be after expiry to."];
        return errors;
    }

    private static void ValidateText(string? value, int maxLength, string field, string label, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) errors[field] = [$"{label} is required."];
        else if (value.Trim().Length > maxLength) errors[field] = [$"{label} is too long."];
    }
}

public sealed class InsurancePolicyValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class DuplicateInsurancePolicyException : Exception;
